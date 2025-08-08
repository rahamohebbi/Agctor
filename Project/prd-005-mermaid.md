# Agctor System Architecture Diagram

This document describes where to find and how to render the architecture diagram.

- Source of truth (Mermaid): `Project/mermaid/diagram.mermaid`
- HTML visualizer (loads the source directly): `Project/mermaid/index.html`

Open the HTML visualizer in a browser (e.g., via a local server) to view the diagram. The page dynamically fetches `diagram.mermaid` and renders it, ensuring there is only one source of truth.

Optional: Export to SVG/PNG using Mermaid CLI

```bash
# Install if needed
npm install -g @mermaid-js/mermaid-cli

# Render SVG
mmdc -i Project/mermaid/diagram.mermaid -o Project/mermaid/diagram.svg

# Render PNG
mmdc -i Project/mermaid/diagram.mermaid -o Project/mermaid/diagram.png
```

Notes
- Do not paste or duplicate the diagram in this file. Always edit `Project/mermaid/diagram.mermaid`.
- The visualizer `Project/mermaid/index.html` is configured to load the diagram file directly and render it with Mermaid in the browser.
