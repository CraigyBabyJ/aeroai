import argparse
import json
import os
from http.server import HTTPServer, BaseHTTPRequestHandler
from socketserver import ThreadingMixIn

from faster_whisper import WhisperModel


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--model", type=str, default="jacktol/whisper-medium.en-fine-tuned-for-ATC-faster-whisper")
    return parser.parse_args()


class ThreadedHTTPServer(ThreadingMixIn, HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


class Handler(BaseHTTPRequestHandler):
    model = None
    model_name = None
    model_device = None

    def log_message(self, format, *args):
        # Keep it concise for debug logs
        print("[whisper-fast] " + format % args)

    def _json(self, code, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path != "/health":
            self._json(404, {"ok": False, "error": "not found"})
            return
        self._json(200, {"ok": True, "model": self.model_name, "device": self.model_device})

    def _load_cpu_fallback(self):
        try:
            print("[whisper-fast] Reloading model on CPU fallback...")
            Handler.model = WhisperModel(Handler.model_name.replace(" (cuda-failed)", ""), device="cpu", compute_type="int8")
            Handler.model_device = "cpu"
            print("[whisper-fast] CPU fallback loaded")
            return True
        except Exception as ex:
            print(f"[whisper-fast] CPU fallback failed: {ex}")
            return False

    def do_POST(self):
        if self.path != "/transcribe":
            self._json(404, {"ok": False, "error": "not found"})
            return

        try:
            length = int(self.headers.get("Content-Length", "0"))
        except Exception:
            length = 0

        body = self.rfile.read(length) if length > 0 else b""
        try:
            payload = json.loads(body.decode("utf-8"))
        except Exception:
            self._json(400, {"ok": False, "error": "invalid json"})
            return

        wav_path = payload.get("wavPath")
        if not wav_path or not os.path.exists(wav_path):
            self._json(400, {"ok": False, "error": "missing or invalid wavPath"})
            return

        try:
            segments, info = self.model.transcribe(
                wav_path,
                language="en",
                beam_size=1,
                best_of=1,
                vad_filter=True,
                condition_on_previous_text=False,
            )
            text_parts = [seg.text.strip() for seg in segments if seg.text]
            transcript = " ".join(text_parts).strip()
            self._json(200, {"ok": True, "text": transcript, "language": info.language or "en"})
        except Exception as ex:
            err = str(ex)
            print(f"[whisper-fast] Transcribe failed on {Handler.model_device}: {err}")
            # Try CPU fallback once if we were on CUDA
            if Handler.model_device == "cuda" and self._load_cpu_fallback():
                try:
                    segments, info = self.model.transcribe(
                        wav_path,
                        language="en",
                        beam_size=1,
                        best_of=1,
                        vad_filter=True,
                        condition_on_previous_text=False,
                    )
                    text_parts = [seg.text.strip() for seg in segments if seg.text]
                    transcript = " ".join(text_parts).strip()
                    self._json(200, {"ok": True, "text": transcript, "language": info.language or "en", "fallback": "cpu"})
                    return
                except Exception as ex2:
                    print(f"[whisper-fast] CPU fallback also failed: {ex2}")
                    self._json(500, {"ok": False, "error": str(ex2)})
                    return
            self._json(500, {"ok": False, "error": err})


def main():
    args = parse_args()
    model_name = args.model
    print(f"[whisper-fast] loading model {model_name}")
    try:
        Handler.model = WhisperModel(model_name, device="cuda", compute_type="float16")
        Handler.model_name = model_name
        Handler.model_device = "cuda"
        print("[whisper-fast] CUDA load successful")
    except Exception as ex:
        print(f"[whisper-fast] CUDA load failed ({ex}); falling back to CPU")
        Handler.model = WhisperModel(model_name, device="cpu", compute_type="int8")
        Handler.model_name = model_name + " (cpu)"
        Handler.model_device = "cpu"
    server = ThreadedHTTPServer(("127.0.0.1", args.port), Handler)
    print(f"[whisper-fast] listening on 127.0.0.1:{args.port}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
