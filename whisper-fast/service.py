import argparse
import json
import os
import time
from http.server import HTTPServer, BaseHTTPRequestHandler
from socketserver import ThreadingMixIn

from faster_whisper import WhisperModel


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8766)
    parser.add_argument("--model", type=str, default="jacktol/whisper-medium.en-fine-tuned-for-ATC-faster-whisper")
    parser.add_argument("--device", type=str, default=os.getenv("WHISPER_FAST_DEVICE", "auto"))
    parser.add_argument("--compute-type", type=str, default=os.getenv("WHISPER_FAST_COMPUTE_TYPE", "auto"))
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
    
    def log_request(self, code='-', size='-'):
        """Override base class log_request to match signature"""
        # We log requests in do_GET/do_POST instead
        pass
    
    def log_incoming_request(self, method, path):
        """Log incoming requests with details"""
        addr = self.client_address
        print(f"[whisper-fast] {method} {path} from {addr[0]}:{addr[1]}")

    def _json(self, code, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)
        try:
            self.wfile.flush()
        except Exception:
            pass

    def do_GET(self):
        start_time = time.time()
        self.log_incoming_request("GET", self.path)
        try:
            if self.path != "/health":
                print(f"[whisper-fast] GET {self.path} -> 404 (not found)")
                self._json(404, {"ok": False, "error": "not found"})
                return
            
            model_info = {
                "ok": True,
                "model": Handler.model_name,
                "device": Handler.model_device
            }
            elapsed = (time.time() - start_time) * 1000
            print(f"[whisper-fast] GET /health -> 200 (model={Handler.model_name}, device={Handler.model_device}, {elapsed:.1f}ms)")
            self._json(200, model_info)
        except Exception as ex:
            elapsed = (time.time() - start_time) * 1000
            print(f"[whisper-fast] GET /health -> ERROR after {elapsed:.1f}ms: {ex}")
            import traceback
            traceback.print_exc()
            try:
                self._json(500, {"ok": False, "error": str(ex)})
            except Exception as e2:
                print(f"[whisper-fast] Failed to send error response: {e2}")

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
        start_time = time.time()
        self.log_incoming_request("POST", self.path)
        try:
            if self.path != "/transcribe":
                print(f"[whisper-fast] POST {self.path} -> 404 (not found)")
                self._json(404, {"ok": False, "error": "not found"})
                return

            try:
                length = int(self.headers.get("Content-Length", "0"))
            except Exception as e:
                print(f"[whisper-fast] Failed to parse Content-Length: {e}")
                length = 0

            body = self.rfile.read(length) if length > 0 else b""
            print(f"[whisper-fast] Received {len(body)} bytes")
            
            try:
                payload = json.loads(body.decode("utf-8"))
            except Exception as e:
                print(f"[whisper-fast] Invalid JSON payload: {e}")
                self._json(400, {"ok": False, "error": "invalid json"})
                return

            wav_path = payload.get("wavPath")
            initial_prompt = payload.get("initialPrompt")
            
            print(f"[whisper-fast] Transcribe request: wavPath={wav_path}, hasPrompt={bool(initial_prompt)}")
            
            if not wav_path:
                print(f"[whisper-fast] Missing wavPath in request")
                self._json(400, {"ok": False, "error": "missing or invalid wavPath"})
                return
                
            if not os.path.exists(wav_path):
                print(f"[whisper-fast] WAV file not found: {wav_path}")
                self._json(400, {"ok": False, "error": "missing or invalid wavPath"})
                return

            file_size = os.path.getsize(wav_path)
            print(f"[whisper-fast] WAV file size: {file_size} bytes")
            
            if Handler.model is None:
                print(f"[whisper-fast] ERROR: Model is None! Cannot transcribe.")
                self._json(500, {"ok": False, "error": "model not loaded"})
                return

            try:
                transcribe_start = time.time()
                options = dict(
                    language="en",
                    beam_size=5,  # Increased from 1 for better accuracy
                    best_of=5,    # Increased from 1 for better sampling
                    vad_filter=True,
                    condition_on_previous_text=True,  # Enable context from previous text
                )
                if isinstance(initial_prompt, str) and initial_prompt.strip():
                    options["initial_prompt"] = initial_prompt.strip()
                    print(f"[whisper-fast] Using initial prompt: {initial_prompt[:50]}...")
                
                print(f"[whisper-fast] Starting transcription on {Handler.model_device}...")
                segments, info = Handler.model.transcribe(wav_path, **options)
                transcribe_elapsed = time.time() - transcribe_start
                
                text_parts = [seg.text.strip() for seg in segments if seg.text]
                transcript = " ".join(text_parts).strip()
                
                total_elapsed = (time.time() - start_time) * 1000
                print(f"[whisper-fast] Transcription complete: {len(transcript)} chars, language={info.language}, transcribe={transcribe_elapsed:.2f}s, total={total_elapsed:.1f}ms")
                print(f"[whisper-fast] Transcript: \"{transcript[:100]}{'...' if len(transcript) > 100 else ''}\"")
                
                self._json(200, {"ok": True, "text": transcript, "language": info.language or "en"})
            except Exception as ex:
                err = str(ex)
                elapsed = (time.time() - start_time) * 1000
                print(f"[whisper-fast] Transcribe failed on {Handler.model_device} after {elapsed:.1f}ms: {err}")
                import traceback
                traceback.print_exc()
                
                # Try CPU fallback once if we were on CUDA
                if Handler.model_device == "cuda" and self._load_cpu_fallback():
                    try:
                        print(f"[whisper-fast] Retrying transcription on CPU fallback...")
                        fallback_start = time.time()
                        options = dict(
                            language="en",
                            beam_size=5,  # Increased from 1 for better accuracy
                            best_of=5,    # Increased from 1 for better sampling
                            vad_filter=True,
                            condition_on_previous_text=True,  # Enable context from previous text
                        )
                        if isinstance(initial_prompt, str) and initial_prompt.strip():
                            options["initial_prompt"] = initial_prompt.strip()
                        segments, info = Handler.model.transcribe(wav_path, **options)
                        fallback_elapsed = time.time() - fallback_start
                        
                        text_parts = [seg.text.strip() for seg in segments if seg.text]
                        transcript = " ".join(text_parts).strip()
                        
                        total_elapsed = (time.time() - start_time) * 1000
                        print(f"[whisper-fast] CPU fallback success: {len(transcript)} chars, {fallback_elapsed:.2f}s, total={total_elapsed:.1f}ms")
                        self._json(200, {"ok": True, "text": transcript, "language": info.language or "en", "fallback": "cpu"})
                        return
                    except Exception as ex2:
                        fallback_elapsed = (time.time() - start_time) * 1000
                        print(f"[whisper-fast] CPU fallback also failed after {fallback_elapsed:.1f}ms: {ex2}")
                        import traceback
                        traceback.print_exc()
                        self._json(500, {"ok": False, "error": str(ex2)})
                        return
                self._json(500, {"ok": False, "error": err})
        except Exception as ex:
            elapsed = (time.time() - start_time) * 1000
            print(f"[whisper-fast] POST request handler error after {elapsed:.1f}ms: {ex}")
            import traceback
            traceback.print_exc()
            try:
                self._json(500, {"ok": False, "error": str(ex)})
            except Exception as e2:
                print(f"[whisper-fast] Failed to send error response: {e2}")


def main():
    args = parse_args()
    model_name = args.model
    device = (args.device or "auto").strip().lower()
    compute_type = (args.compute_type or "auto").strip().lower()
    print(f"[whisper-fast] loading model {model_name}")

    def cpu_compute():
        return compute_type if compute_type != "auto" else "int8"

    def cuda_compute():
        return compute_type if compute_type != "auto" else "float16"

    if device == "cpu":
        Handler.model = WhisperModel(model_name, device="cpu", compute_type=cpu_compute())
        Handler.model_name = model_name + " (cpu)"
        Handler.model_device = "cpu"
        print("[whisper-fast] CPU load forced")
    else:
        try:
            Handler.model = WhisperModel(model_name, device="cuda", compute_type=cuda_compute())
            Handler.model_name = model_name
            Handler.model_device = "cuda"
            print("[whisper-fast] CUDA load successful")
        except Exception as ex:
            print(f"[whisper-fast] CUDA load failed ({ex}); falling back to CPU")
            Handler.model = WhisperModel(model_name, device="cpu", compute_type=cpu_compute())
            Handler.model_name = model_name + " (cpu)"
            Handler.model_device = "cpu"
    server = ThreadedHTTPServer(("127.0.0.1", args.port), Handler)
    print(f"[whisper-fast] listening on 127.0.0.1:{args.port}")
    print(f"[whisper-fast] Model: {Handler.model_name}, Device: {Handler.model_device}")
    print(f"[whisper-fast] Ready to accept requests")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print(f"[whisper-fast] Shutting down...")
    finally:
        server.server_close()
        print(f"[whisper-fast] Server closed")


if __name__ == "__main__":
    main()
