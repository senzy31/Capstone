"""Registry mapping each TemplateType to its layout builder + display info."""

from __future__ import annotations

from typing import Callable, Dict

from app.models import ResumeData, TemplateInfo, TemplateType
from app.templates import functional, harvard, reverse_chronological
from app.templates.base import ResumeLayout

_BUILDERS: Dict[TemplateType, Callable[[ResumeData], ResumeLayout]] = {
    TemplateType.HARVARD: harvard.build_layout,
    TemplateType.REVERSE_CHRONOLOGICAL: reverse_chronological.build_layout,
    TemplateType.FUNCTIONAL: functional.build_layout,
}

TEMPLATE_INFO = [
    TemplateInfo(
        id=TemplateType.HARVARD,
        name="Harvard",
        description="Classic, conservative format that leads with education. "
                    "Best for academic, research, or early-career resumes.",
    ),
    TemplateInfo(
        id=TemplateType.REVERSE_CHRONOLOGICAL,
        name="Reverse Chronological",
        description="The most widely used format - leads with your most recent "
                    "experience. Best for candidates with a steady work history.",
    ),
    TemplateInfo(
        id=TemplateType.FUNCTIONAL,
        name="Functional",
        description="Groups content by skill rather than by job timeline. "
                    "Best for career changers or resumes with employment gaps.",
    ),
]


def build_layout(resume: ResumeData) -> ResumeLayout:
    template = resume.template or TemplateType.REVERSE_CHRONOLOGICAL
    return _BUILDERS[template](resume)
