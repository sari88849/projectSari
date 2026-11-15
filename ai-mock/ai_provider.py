"""AI provider abstraction.

A provider receives the user's natural-language request and the metadata context
(entities, fields, Hebrew labels, synonyms, operators, response schema) and returns a
dict that follows shared/ai-response.schema.json.

To use a real model, add a provider class (e.g. one that sends context["instructions"],
the context and the prompt to an LLM API and parses its JSON answer) and register it in
create_provider(). Neither the HTTP server contract nor the .NET backend changes.
"""
from abc import ABC, abstractmethod
from typing import Any, Dict


class AiProvider(ABC):
    name = "base"

    @abstractmethod
    def generate(self, prompt: str, context: Dict[str, Any]) -> Dict[str, Any]:
        """Return a proposal dict that matches shared/ai-response.schema.json."""


def create_provider(name: str) -> AiProvider:
    """Provider factory. Only the mock exists today; real providers plug in here."""
    from mock_provider import MockAiProvider

    providers = {"mock": MockAiProvider}
    if name not in providers:
        raise ValueError("Unknown AI provider '{}'. Available: {}".format(name, ", ".join(providers)))
    return providers[name]()
