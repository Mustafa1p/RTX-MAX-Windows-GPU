# Desktop reference lock

Designing a Windows x64 diagnostic and setup workbench for RTX owners. The primary
task is to identify the GPU, understand unlock eligibility, measure matched compute
before/after and prepare only a qualified firmware workflow. Direct build uses the
existing RTX-MAX repository artwork as the concrete visual target.

Live Refero styles/screens/flows were queried first, but returned NO_SUBSCRIPTION.
The fallback research is the installed Refero skill's typography, color and craft
references, plus Microsoft's NavigationView guidance:
https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview

| Decision | Source | Rule and purpose |
| --- | --- | --- |
| Navy canvas, mint action/status accent | Existing assets/banner.png | Preserve project identity; mint does not imply universal unlock support |
| Neutral Segoe UI, compact factual labels | Refero typography guide | A work tool needs legibility and scanning rather than display typography |
| Four persistent navigation destinations | Microsoft navigation guidance | Keep health, measurements, setup and exports discoverable |
| Color plus written state/reason | Refero color/craft guides | Never encode compatibility by green/red alone |
| GPU identity as the main instrument panel | User's support-check task | Actual detected facts precede any setup action |
| Empty before/after states until real measurements | User's before/after request | No illustrative/reference values presented as live results |
| Gated, explained setup steps | Existing INSTALL.md / runtime restrictions | A signature match does not approve a different PC's firmware topology |
| Local reports, explicit exports | Existing privacy scope | No automatic report upload, recovery-key collection or account connection |

Tokens: canvas #0D1724, sidebar #111E2D, surface #152438, border #2A3D52,
text #EEF4FA, secondary #A8BBD0, mint #7CE3AC, cyan #87CFFF, amber #F5C779,
error #FF9999. Body 14, metadata 12, section 20, page title 30, numeric readout 32.
Spacing 8/12/16/24/32. Radius 8. No decorative gradients, fake product screenshots,
glowing charts or generated GPU photography. Charts are code-native and data-bound.

Validation target: render the actual WPF window at 1280x840 and 1080x720, inspect
spacing/text/contrast and page states; independently test backend execution and
refusal paths. Rendering an empty/busy/error state is not hardware qualification.
