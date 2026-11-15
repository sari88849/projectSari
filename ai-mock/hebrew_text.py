"""Very small Hebrew text helpers for the rule-based mock.

Hebrew attaches prefixes to words (ו ה ש מ ל ב כ: "and", "the", "that", "from", "to", "in", "as")
and inflects for plural/gender ("פעיל", "פעילה", "פעילים", "פעילות"). The mock compares words by
stripping up to two prefix letters and a few common suffixes. This is intentionally simple:
a real language model replaces all of this.
"""
import re
from typing import List

PREFIX_LETTERS = set("והשמלבכ")
SUFFIXES = ("ים", "ות", "ה", "ת", "ן", "ם")
_PUNCTUATION = re.compile(r"[,.;:!?()\[\]{}\"'״׳`]")
_THOUSANDS = re.compile(r"(\d),(\d{3})")
_HEBREW_HYPHEN = re.compile(r"(?<=[֐-׿])-|-(?=[֐-׿])")
_DMY_DATE = re.compile(r"\b(\d{1,2})/(\d{1,2})/(\d{4})\b")

NUMBER = re.compile(r"^\d+(\.\d+)?$")
ISO_DATE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


def tokenize(text: str) -> List[str]:
    """Split into words; keeps numbers (15,000 -> 15000) and dates (31/12/2020 -> 2020-12-31) intact."""
    text = _THOUSANDS.sub(r"\1\2", text)
    text = _DMY_DATE.sub(lambda m: "{}-{:0>2}-{:0>2}".format(m.group(3), m.group(2), m.group(1)), text)
    text = _HEBREW_HYPHEN.sub(" ", text)          # "מ-15000" -> "מ 15000", dates keep their hyphens
    text = _PUNCTUATION.sub(" ", text)
    return [t for t in text.split() if t]


def stem(word: str) -> str:
    """Drop the definite article and one common suffix: 'הפעילות' -> 'פעיל'."""
    while word.startswith("ה") and len(word) > 2:
        word = word[1:]
    for suffix in SUFFIXES:
        if word.endswith(suffix) and len(word) - len(suffix) >= 2:
            return word[: -len(suffix)]
    return word


def variants(token: str) -> List[str]:
    """The token itself plus the token without up to two leading prefix letters."""
    result = [token]
    current = token
    for _ in range(2):
        if len(current) > 2 and current[0] in PREFIX_LETTERS:
            current = current[1:]
            result.append(current)
        else:
            break
    return result


def word_matches(token: str, word: str) -> bool:
    target = stem(word)
    return any(stem(v) == target for v in variants(token))


def phrase_matches(tokens: List[str], start: int, phrase: List[str]) -> bool:
    if start + len(phrase) > len(tokens):
        return False
    return all(word_matches(tokens[start + k], phrase[k]) for k in range(len(phrase)))


def any_word(token: str, words) -> bool:
    return any(word_matches(token, w) for w in words)
