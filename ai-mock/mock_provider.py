"""Rule-based mock of an AI model.

It understands simple Hebrew requests by matching words against the metadata CONTEXT it receives
(field labels, synonyms, boolean labels, entity and relationship names). It contains no field names
of its own, so it keeps working when the catalog changes - like a real model would.

Supported phrasing (examples):
  "עובדים מתחת לגיל 18 עם שכר חודשי מעל 15000"             -> Age < 18 AND Salary > 15000
  "עובדים שתאריך סיום העסקה מוקדם מתאריך תחילת העסקה"      -> EndDate < StartDate (field comparison)
  "עובדים פעילים במחלקה לא פעילה"                          -> IsActive = true AND Department.IsActive = false
  "עובדים שחסר להם תעודת זהות או דואר אלקטרוני"            -> NationalId empty OR Email empty
  "עובדים עם שכר חודשי גבוה משכר מקסימלי לתפקיד"            -> Salary > Position.MaxSalary
"""
from typing import Any, Dict, List, Optional, Tuple

from ai_provider import AiProvider
from hebrew_text import ISO_DATE, NUMBER, any_word, phrase_matches, tokenize, word_matches

LESS = ["מתחת", "קטן", "פחות", "נמוך", "מוקדם", "לפני"]
GREATER = ["מעל", "גדול", "יותר", "גבוה", "מאוחר", "אחרי"]
EQUALS = ["שווה"]
NOT_EQUALS = ["שונה"]
EMPTY_MARKERS = ["חסר", "ריק", "ללא", "בלי", "אין"]
NEGATIONS = ["לא", "אינו", "אינה", "אינם", "אינן"]
OR_WORDS = ["או"]
STOPWORDS = {"עם", "של", "להם", "לו", "לה", "את", "על", "כל", "אשר", "גם", "וגם", "הם", "היא", "הוא",
             "שלהם", "שלו", "שלה", "מ", "ל", "ב", "ש", "ו", "ה", "כאשר", "אשר", "רשומות", "בדיקה"}


class _Field:
    def __init__(self, data: Dict[str, Any]):
        self.path = data["path"]                       # e.g. "Department.IsActive"
        self.data_type = data["dataType"]
        self.true_label = data.get("trueLabel")
        self.hops = self.path.count(".")
        self.prefix = self.path.rsplit(".", 1)[0] if self.hops else ""
        names = [data["fieldLabel"]] + list(data.get("synonyms") or [])
        self.phrases = [n.split() for n in names if n and n.strip()]


class MockAiProvider(AiProvider):
    name = "mock"

    def generate(self, prompt: str, context: Dict[str, Any]) -> Dict[str, Any]:
        tokens = tokenize(prompt)
        entity = self._pick_entity(tokens, context)
        if entity is None:
            return self._unsupported(prompt, "", ["לא נמצאה ישות מתאימה בהקשר שהתקבל"])

        parser = _Parser(tokens, entity)
        conditions, unknown = parser.parse()
        if not conditions:
            return self._unsupported(prompt, entity["key"], ["לא זוהו תנאים בבקשה. נסו לנסח עם שמות שדות מוכרים."])

        logic, nodes = _combine(conditions)
        meaningful = [t for t in unknown if t not in STOPWORDS]
        confidence = round(max(0.3, 0.95 - 0.1 * len(meaningful)), 2)
        notes = ["תשובה זו נוצרה על ידי מנגנון Mock מבוסס כללים (ללא מודל AI אמיתי)."]
        if meaningful:
            notes.append("מילים שלא זוהו: " + ", ".join(meaningful[:10]))

        return {
            "status": "ok",
            "name": prompt.strip()[:80],
            "description": prompt.strip(),
            "rootEntity": entity["key"],
            "logic": logic,
            "conditions": nodes,
            "confidence": confidence,
            "notes": notes,
        }

    @staticmethod
    def _pick_entity(tokens: List[str], context: Dict[str, Any]) -> Optional[Dict[str, Any]]:
        entities = context.get("entities") or []
        requested = context.get("requestedRootEntity")
        if requested:
            return next((e for e in entities if e["key"] == requested), None)
        # The entity mentioned first in the request ("עובדים ..." / "מחלקות ...").
        best, best_index = None, len(tokens) + 1
        for entity in entities:
            names = [entity["displayName"], entity["displayNamePlural"]]
            for i, token in enumerate(tokens):
                if i < best_index and any_word(token, names):
                    best, best_index = entity, i
                    break
        return best or (entities[0] if entities else None)

    @staticmethod
    def _unsupported(prompt: str, entity_key: str, notes: List[str]) -> Dict[str, Any]:
        return {"status": "unsupported", "name": prompt.strip()[:80], "description": prompt.strip(),
                "rootEntity": entity_key, "logic": "And", "conditions": [], "confidence": 0, "notes": notes}


class _Parser:
    """Left-to-right scan with a tiny state machine: entity context, pending operator, current field."""

    def __init__(self, tokens: List[str], entity: Dict[str, Any]):
        self.tokens = tokens
        self.fields = [_Field(f) for f in entity.get("fields") or []]
        # Mentions that set the "current entity" context: the root itself and its relationships.
        self.contexts: List[Tuple[List[str], str]] = []
        for name in (entity["displayName"], entity["displayNamePlural"]):
            self.contexts.append((name.split(), ""))
        for rel in entity.get("relationships") or []:
            words = rel["displayName"].split()
            self.contexts.append((words, rel["key"]))
            if len(words) > 1:
                self.contexts.append((words[:1], rel["key"]))

    def parse(self):
        conditions: List[Tuple[str, Dict[str, Any]]] = []   # (connector to previous, condition)
        unknown: List[str] = []
        ctx = ""
        pending_op: Optional[str] = None
        current: Optional[_Field] = None
        empty_mode = False
        negate = False
        connector = "And"

        def add(condition: Dict[str, Any]):
            nonlocal connector
            conditions.append((connector, condition))
            connector = "And"

        i = 0
        while i < len(self.tokens):
            t = self.tokens[i]

            if t in OR_WORDS:
                connector = "Or"
                i += 1
                continue
            if NUMBER.match(t) or ISO_DATE.match(t):
                if current is not None and pending_op:
                    add({"field": current.path, "operator": pending_op, "value": _value(t)})
                    current, pending_op = None, None
                else:
                    unknown.append(t)
                i += 1
                continue
            if t in NEGATIONS:
                negate = True
                i += 1
                continue
            if any_word(t, EMPTY_MARKERS):
                empty_mode = True
                i += 1
                continue
            op = _comparator(t)
            if op:
                pending_op = op
                empty_mode = False
                i += 1
                continue

            ctx_match = self._match_context(i)
            field_match = self._match_field(i, ctx)
            if ctx_match and (field_match is None or ctx_match[1] >= field_match[1]):
                ctx = ctx_match[0]
                i += ctx_match[1]
                continue

            # A status word ("פעילים", "לא פעילה") means "boolean field of the entity in context = true/false".
            # Checked before single-word field matches, because status words are also boolean synonyms.
            bool_field = self._match_boolean(t, ctx) if not empty_mode and pending_op is None else None
            if bool_field and (field_match is None or field_match[1] == 1):
                add({"field": bool_field.path, "operator": "Equals", "value": not negate})
                negate = False
                i += 1
                continue

            if field_match:
                field, length = field_match
                if empty_mode:
                    add({"field": field.path, "operator": "IsEmpty"})
                elif field.data_type == "Boolean" and pending_op is None:
                    # e.g. synonym "מחלקה לא פעילה": the phrase itself states the value.
                    span = self.tokens[i:i + length]
                    is_false = negate or any(tok in NEGATIONS for tok in span)
                    add({"field": field.path, "operator": "Equals", "value": not is_false})
                elif current is not None and pending_op and field.path != current.path:
                    add({"field": current.path, "operator": pending_op, "compareToField": field.path})
                    current, pending_op = None, None
                else:
                    current = field
                i += length
                negate = False
                continue

            unknown.append(t)
            negate = False
            i += 1

        return conditions, unknown

    def _match_context(self, i: int) -> Optional[Tuple[str, int]]:
        best = None
        for words, key in self.contexts:
            if phrase_matches(self.tokens, i, words) and (best is None or len(words) > best[1]):
                best = (key, len(words))
        return best

    def _match_field(self, i: int, ctx: str) -> Optional[Tuple[_Field, int]]:
        """Longest label/synonym match; ties prefer the current context, then fewer relationship hops."""
        best = None
        best_rank = None
        for field in self.fields:
            for phrase in field.phrases:
                if phrase_matches(self.tokens, i, phrase):
                    rank = (len(phrase), 1 if field.prefix == ctx else 0, -field.hops)
                    if best_rank is None or rank > best_rank:
                        best, best_rank = (field, len(phrase)), rank
        return best

    def _match_boolean(self, token: str, ctx: str) -> Optional[_Field]:
        """A status word ("פעילים", "פעילה") refers to the boolean field of the entity in context."""
        candidates = [f for f in self.fields if f.data_type == "Boolean" and f.true_label
                      and all(word_matches(token, w) for w in f.true_label.split()[:1])]
        if not candidates:
            return None
        in_ctx = [f for f in candidates if f.prefix == ctx]
        return (in_ctx or sorted(candidates, key=lambda f: f.hops))[0]


def _comparator(token: str) -> Optional[str]:
    for words, op in ((LESS, "LessThan"), (GREATER, "GreaterThan"), (EQUALS, "Equals"), (NOT_EQUALS, "NotEquals")):
        if token in words or any(word_matches(token, w) for w in words):
            return op
    return None


def _value(token: str):
    if ISO_DATE.match(token):
        return token
    number = float(token)
    return int(number) if number.is_integer() else number


def _combine(conditions: List[Tuple[str, Dict[str, Any]]]):
    """'A and B or C' -> A AND (B OR C): runs joined by 'או' become OR groups, runs are ANDed."""
    runs: List[List[Dict[str, Any]]] = []
    for connector, condition in conditions:
        if connector == "Or" and runs:
            runs[-1].append(condition)
        else:
            runs.append([condition])
    if len(runs) == 1:
        return ("Or" if len(runs[0]) > 1 else "And"), runs[0]
    nodes = [run[0] if len(run) == 1 else {"logic": "Or", "conditions": run} for run in runs]
    return "And", nodes
