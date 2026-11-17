# Brand assets

Original vector artwork for **NuntivusRedact**, a library intended for use by the parent Nuntivus project and other .NET applications. The document, folded corner and redaction bars are a distinct mark, not a reproduction of the parent project's logo. The shared dark/purple/blue palette creates a family resemblance.

| Asset | Canvas | Use |
| --- | --- | --- |
| [Logo](nuntivus-redact-logo.svg) | 256 × 256 | Project icon, documentation and avatar source |
| [Banner](nuntivus-redact-banner.svg) | 1280 × 480 | GitHub README header and documentation |

![NuntivusRedact logo: a purple document with blue redaction accents](nuntivus-redact-logo.svg)

## Palette

| Role | Color |
| --- | --- |
| Background | `#0d1117` |
| Purple | `#6d5dfc` |
| Deep purple | `#302388` |
| Blue | `#008cff` |
| Highlight | `#39c6ff` |
| Primary text | `#f0f6fc` |

The SVGs are self-contained: no JavaScript, remote images, downloaded fonts or build-time asset dependencies. Each includes a title, description and accessible image role. The banner uses system-font fallbacks, so text metrics can vary slightly across renderers. Preserve aspect ratio, readable contrast and clear space around the mark. Use descriptive alternative text when embedding the files.

## Preview and export

Open either SVG in a modern browser or vector editor to inspect it at desktop and mobile widths. The README embeds the banner at a responsive width; the logo is available separately for compact layouts.

For GitHub's social preview, export a **PNG or JPEG**, preferably on a 1280 × 640 canvas with the banner centered and dark padding above/below. Upload it in the repository's settings after import. GitHub does not accept an SVG as the social-preview upload. No raster export or upload is implied by these source assets.

## License and meaning

These original assets are distributed under the repository's [Apache-2.0 license](../../LICENSE). No separate trademark registration or parent-project endorsement is asserted. The redaction motif is an illustration, not proof of complete sensitive-data discovery, anonymization or secure processing of unsupported formats. Do not use the artwork to imply such guarantees.

See the [GitHub setup guide](../GITHUB_SETUP.md) for badges and repository configuration.
