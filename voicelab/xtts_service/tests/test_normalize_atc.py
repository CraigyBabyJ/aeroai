from xtts_service.normalize_atc import normalize_atc


def test_acronyms_and_delimiters():
    text = "QNH|ATIS\nvfr"
    normalized, changed = normalize_atc(text)
    assert normalized == "Q N H|A T I S\nV F R"
    assert changed


def test_runway_and_heading():
    text = "Runway 5, heading 50"
    normalized, changed = normalize_atc(text)
    assert normalized == "runway zero five, heading zero five zero"
    assert changed


def test_runway_with_side_and_squawk():
    text = "RWY 27L squawk 462"
    normalized, changed = normalize_atc(text)
    assert normalized == "rwy two seven left squawk zero four six two"
    assert changed


def test_heading_zeros():
    text = "hdg 005"
    normalized, changed = normalize_atc(text)
    assert normalized == "hdg zero zero five"
    assert changed


def test_flight_level():
    text = "FL350"
    normalized, changed = normalize_atc(text)
    assert normalized == "flight level three five zero"
    assert changed


def test_qnh_digits():
    text = "QNH 1016"
    normalized, changed = normalize_atc(text)
    assert "Q N H one zero one six" in normalized
    assert changed


def test_frequency_and_delimiters():
    text = "121.800|118.10\nQFE 999"
    normalized, changed = normalize_atc(text)
    assert "one two one decimal eight" in normalized
    assert "one one eight decimal one" in normalized
    assert "|" in normalized and "\n" in normalized
    assert "Q F E nine nine nine" in normalized
