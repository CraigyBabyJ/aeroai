# VATSIM ATC Communications – Beginner Walkthrough

*A friendly, step‑by‑step guide to talking to ATC on VATSIM, with examples.*

---

## Before You Talk to ATC

Before calling **any** controller:
- File your flight plan in **vPilot** (Flightplan tab)
- Ensure route, aircraft, cruise level, and destination are correct
- ATC can’t help if they can’t see your plan — no clairvoyance, sadly

If **no ATC** is online:
- Tune **UNICOM 122.800**
- Announce your intentions clearly (taxiing, departing, landing, etc.)

Useful tools:
- vPilot controller list (left panel)
- https://www.vattastic.com

---

## What Each ATC Position Does

### Area / Centre Control
- Manages en‑route traffic within a sector
- May operate **top‑down** (handling airports too)

### Approach
- Manages arrivals and departures
- Issues STARs or vectors

### Tower
- Takeoff & landing clearances

### Ground
- Taxi instructions, stands, holding points

### Delivery
- Issues IFR clearances

### ATIS
- Automated airport info (weather, runway, transition)
- Always listen first

### UNICOM (122.800)
- No ATC present
- Used to self‑announce intentions

---

## Who Do I Talk To?

- **No ATC at all:** UNICOM only
- **Delivery only:** Get clearance → UNICOM
- **Ground only:** Clearance, push/start, taxi
- **Tower only:** Clearance, push/start, taxi, takeoff/landing
- **Approach only:** Everything + approach
- **Centre online:**
  - Check if operating **top‑down**
  - If yes → they handle *everything*
  - If no → only above their specified FL

---

## How to Talk to ATC – Departure

### 1. ATIS
- Always listen first
- Note information code (Alpha, Bravo, etc.)
- If no ATIS: omit it unless instructed

---

### 2. Delivery – IFR Clearance

**With ATIS:**
```
Airport Delivery, this is CALLSIGN, at stand STAND, AIRCRAFT TYPE,
request IFR clearance to DESTINATION, with information ATIS.
```

**Example:**
```
Gatwick Delivery, this is EZY2281, at stand 109, A320,
request IFR clearance to Frankfurt, with information Alpha.
```

**ATC:**
```
Cleared to DESTINATION via SID, squawk CODE
```

**Readback:**
```
Cleared to DESTINATION via SID, squawk CODE, CALLSIGN
```

---

## Ground Operations

### Push & Start
```
Airport Ground, CALLSIGN at stand STAND, AIRCRAFT TYPE,
request push and start.
```

**Readback:**
```
Push and start approved facing DIRECTION, CALLSIGN
```

---

### Taxi
```
Airport Ground, CALLSIGN pushed back from stand STAND,
request taxi.
```

**ATC:** Taxi to holding point via taxiways

**Readback:**
```
Taxi to holding point via TAXIWAYS, CALLSIGN
```

📌 *Use airport charts — taxiways are not vibes-based.*

---

## Tower – Takeoff

### Initial Contact
```
CALLSIGN with you, taxiing to holding point.
```

### Hold Short
```
Taxi and hold holding point, CALLSIGN
```

### Takeoff Clearance
```
Cleared for takeoff runway RUNWAY, CALLSIGN
```

After departure:
- Contact **Centre** if online
- Otherwise monitor **UNICOM 122.800**

---

## En‑Route (Centre)

### First Contact
```
Centre Name, CALLSIGN with you at flight level FL,
inbound WAYPOINT.
```

**ATC:** Identified

### Direct Routing
```
Direct WAYPOINT, CALLSIGN
```

### Top of Descent
```
CALLSIGN approaching top of descent
```

---

## Approach Phase

### Initial Contact
```
CALLSIGN with you descending to flight level FL
```

**ATC may say:**
- Expect STAR arrival
- Expect vectors

**Reply accordingly:**
```
Expect STAR ARRIVAL runway RUNWAY, CALLSIGN
```

---

## Tower – Arrival

Before contacting Tower:
- Listen to arrival ATIS (if available)
- If on vectors, say so

### Contact Tower
```
Arrival Tower, CALLSIGN with STAR/VECTORS,
request landing runway RUNWAY with information ATIS.
```

**ATC:**
```
Cleared to land runway RUNWAY
```

---

## After Landing

- Follow tower exit instructions
- Contact Ground when instructed

### Taxi to Gate
```
Arrival Ground, CALLSIGN with you at holding point,
request taxi to gate.
```

**Readback:**
```
Taxi to stand STAND via TAXIWAYS, CALLSIGN
```

---

## Final Tips

- Speak calmly and confidently
- If unsure: **ask**
- Nobody expects perfection — just effort
- Charts are your best friend
- Everyone was new once ✨

---

*Saved as a beginner-friendly VATSIM reference.*

