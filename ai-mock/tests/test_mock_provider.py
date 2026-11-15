"""Tests for the rule-based mock provider, using the real context exported by the backend
(GET /api/ai/context -> tests/fixtures/context.json)."""
import json
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

from mock_provider import MockAiProvider  # noqa: E402

with open(os.path.join(HERE, "fixtures", "context.json"), encoding="utf-8") as f:
    CONTEXT = json.load(f)

with open(os.path.join(HERE, "..", "..", "shared", "ai-response.schema.json"), encoding="utf-8") as f:
    SCHEMA = json.load(f)

OPERATORS = set(SCHEMA["$defs"]["condition"]["properties"]["operator"]["enum"])
CONDITION_KEYS = set(SCHEMA["$defs"]["condition"]["properties"])
ROOT_KEYS = set(SCHEMA["properties"])


def check_schema(test: unittest.TestCase, response: dict):
    """Minimal structural check against shared/ai-response.schema.json (stdlib has no JSON Schema validator)."""
    test.assertTrue(set(response) <= ROOT_KEYS, set(response) - ROOT_KEYS)
    for key in SCHEMA["required"]:
        test.assertIn(key, response)
    test.assertIn(response["status"], ("ok", "unsupported"))
    test.assertIn(response["logic"], ("And", "Or"))

    def check_node(node):
        if "conditions" in node:
            test.assertEqual(set(node), {"logic", "conditions"})
            test.assertIn(node["logic"], ("And", "Or"))
            test.assertTrue(node["conditions"])
            for child in node["conditions"]:
                check_node(child)
        else:
            test.assertTrue(set(node) <= CONDITION_KEYS, set(node) - CONDITION_KEYS)
            test.assertIn(node["operator"], OPERATORS)
            test.assertIsInstance(node["field"], str)

    for n in response["conditions"]:
        check_node(n)


class MockProviderTests(unittest.TestCase):
    def setUp(self):
        self.provider = MockAiProvider()

    def generate(self, prompt, context=None):
        response = self.provider.generate(prompt, context or CONTEXT)
        check_schema(self, response)
        json.dumps(response, ensure_ascii=False)   # must be serializable
        return response

    def test_age_and_salary(self):
        r = self.generate("עובדים מתחת לגיל 18 עם שכר חודשי מעל 15,000")
        self.assertEqual(r["status"], "ok")
        self.assertEqual(r["rootEntity"], "Employee")
        self.assertEqual(r["logic"], "And")
        self.assertEqual(r["conditions"], [
            {"field": "Age", "operator": "LessThan", "value": 18},
            {"field": "Salary", "operator": "GreaterThan", "value": 15000},
        ])

    def test_field_to_field_comparison(self):
        r = self.generate("עובדים שתאריך סיום העסקה מוקדם מתאריך תחילת העסקה")
        self.assertEqual(r["conditions"], [{"field": "EndDate", "operator": "LessThan", "compareToField": "StartDate"}])

    def test_boolean_status_through_relationship(self):
        r = self.generate("עובדים פעילים במחלקות לא פעילות")
        self.assertEqual(r["conditions"], [
            {"field": "IsActive", "operator": "Equals", "value": True},
            {"field": "Department.IsActive", "operator": "Equals", "value": False},
        ])

    def test_missing_fields_with_or(self):
        r = self.generate("עובדים שחסר להם תעודת זהות או דואר אלקטרוני")
        self.assertEqual(r["logic"], "Or")
        self.assertEqual([c["field"] for c in r["conditions"]], ["NationalId", "Email"])
        self.assertTrue(all(c["operator"] == "IsEmpty" for c in r["conditions"]))

    def test_mixed_and_or_creates_nested_group(self):
        r = self.generate("עובדים פעילים שחסר להם תעודת זהות או דואר אלקטרוני")
        self.assertEqual(r["logic"], "And")
        self.assertEqual(r["conditions"][0], {"field": "IsActive", "operator": "Equals", "value": True})
        self.assertEqual(r["conditions"][1]["logic"], "Or")
        self.assertEqual(len(r["conditions"][1]["conditions"]), 2)

    def test_cross_entity_field_comparison(self):
        r = self.generate("עובדים עם שכר חודשי גבוה משכר מקסימלי לתפקיד")
        self.assertEqual(r["conditions"], [{"field": "Salary", "operator": "GreaterThan", "compareToField": "Position.MaxSalary"}])

    def test_other_root_entity_and_negation_word(self):
        r = self.generate("מחלקות שמנהלן אינו פעיל")
        self.assertEqual(r["rootEntity"], "Department")
        self.assertEqual(r["conditions"], [{"field": "Manager.IsActive", "operator": "Equals", "value": False}])

    def test_date_value_is_iso(self):
        r = self.generate("עובדים עם תאריך תחילת העסקה לפני 01/01/2010")
        self.assertEqual(r["conditions"], [{"field": "StartDate", "operator": "LessThan", "value": "2010-01-01"}])

    def test_requested_root_entity_is_respected(self):
        context = dict(CONTEXT, requestedRootEntity="Employee")
        r = self.generate("מחלקה לא פעילה", context)
        self.assertEqual(r["rootEntity"], "Employee")

    def test_unrelated_request_is_unsupported(self):
        r = self.generate("מה מזג האוויר מחר")
        self.assertEqual(r["status"], "unsupported")
        self.assertEqual(r["conditions"], [])
        self.assertTrue(r["notes"])

    def test_mock_uses_only_context_fields(self):
        """Renaming a label in the context changes what the mock understands - nothing is hard-coded."""
        context = json.loads(json.dumps(CONTEXT))
        for entity in context["entities"]:
            for field in entity["fields"]:
                if field["path"] == "Salary":
                    field["fieldLabel"] = "תגמול"
                    field["synonyms"] = []
        r = self.generate("עובדים עם תגמול מעל 20000", context)
        self.assertEqual(r["conditions"], [{"field": "Salary", "operator": "GreaterThan", "value": 20000}])
        r = self.generate("עובדים עם שכר חודשי מעל 20000", context)
        self.assertNotIn({"field": "Salary", "operator": "GreaterThan", "value": 20000}, r["conditions"])


if __name__ == "__main__":
    unittest.main()
