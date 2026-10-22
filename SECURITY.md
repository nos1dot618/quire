# Security policy

## Supported versions

Only the latest release receives security fixes.

## Reporting a vulnerability

Please do not open a public issue for security problems.

Report them privately through GitHub: open the repository's **Security** tab and choose **Report a vulnerability**. This creates a private advisory that only the maintainers can see.

Please include:

- the Quire version and Windows version,
- what an attacker can do and how (a crafted `.quire` file, a pasted or dropped item, an exported file),
- steps or a sample file that reproduce it, if you have them.

You can expect an acknowledgement within 7 days. We will keep you informed while a fix is prepared, and credit you in the release notes unless you would rather stay anonymous.

## Scope

Quire opens files that other people may have created, so these areas matter most:

- **Opening `.quire` files:** malformed archives, paths that try to escape the archive, oversized manifests, decompression bombs.
- **Links:** Quire only opens `http`, `https` and `mailto` links. Any way to launch another scheme or a program is a vulnerability.
- **Exports:** HTML export removes raw HTML and unsafe link schemes from Markdown. Script injection into an exported page is a vulnerability.
- **Opening images and attachments:** Quire writes them to a temporary folder and only opens image file types with the default viewer.

Quire has no network features, accounts or telemetry. Reports about those therefore mostly concern third-party dependencies (Markdig, PDFsharp/MigraDoc), which we update when fixes are published.
