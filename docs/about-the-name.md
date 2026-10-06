# About the name

**Diana**: *precision guide RNA design.*

- In Spanish, *diana* means "bullseye" (*dar en la diana* = to hit the target), and Diana is the Roman goddess of the hunt:
  precise aim. That is what the tool does: it aims a guide RNA at a sequence.
- It reads as a product name in English too, and pairs naturally with *Frida* as a sister name.
- It says nothing about rare diseases, so it stays valid if the tool is extended to any CRISPR application, whatever the
  disease.

The name is user-facing only: the web UI, the CSV report header (`# Diana report; ...`) and the docs. Code identifiers, project
folders, namespaces (`DiseaseMutationsApp`, `gRNA`) and the repository URL are unchanged. The report header is a `#` comment
line that the loaders skip, so reports written under the previous name ("gRNA Builder") still load.
