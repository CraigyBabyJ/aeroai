from __future__ import annotations

from typing import Dict, List, Optional


class PhrasePacks:
    def __init__(self):
        self.library: Dict[str, Dict[str, List[str]]] = {
            "atc_checkin": {
                "description": "Generic ATC check-in and acknowledgement phrases.",
                "phrases": [
                    "Tower, this is Ghost Rider requesting a flyby.",
                    "Approach, N12345 level at seven thousand, information alpha.",
                    "Ground, ready to taxi from the north apron.",
                    "Cleared for takeoff, runway two seven right.",
                    "Contact departure on one two four point five.",
                    "Roger, holding short of runway three six.",
                    "Negative contact, still looking for traffic.",
                    "Ready for the ILS approach runway one eight.",
                    "Request vectors to final, fuel state four thousand.",
                    "We are established on the localizer.",
                ],
            },
            "callouts": {
                "description": "Cabin and cockpit callouts for quick demos.",
                "phrases": [
                    "Passing through ten thousand feet.",
                    "Minimums, continue.",
                    "Rotate.",
                    "Positive rate, gear up.",
                    "Flaps one, flaps two.",
                    "Speed checked.",
                    "Climb power set.",
                    "Landing checklist complete.",
                    "Cleared to land.",
                    "APU is available.",
                ],
            },
            "greetings": {
                "description": "Friendly samples for casual demo playback.",
                "phrases": [
                    "Welcome to the XTTS Voice Lab demo.",
                    "Here is a sample line in British English.",
                    "We cache audio so repeat lines play instantly.",
                    "Try switching the voice profile for a different flavor.",
                    "Prefetch a phrase pack to warm the cache.",
                    "Thanks for testing, have a great day.",
                ],
            },
        }

    def list_sets(self):
        return [
            {"id": pack_id, "description": data["description"], "count": len(data["phrases"])}
            for pack_id, data in self.library.items()
        ]

    def get_phrases(self, pack_id: str, limit: Optional[int] = None) -> List[str]:
        pack = self.library.get(pack_id)
        if not pack:
            return []
        phrases = pack["phrases"]
        if limit:
            return phrases[: max(1, limit)]
        return phrases

