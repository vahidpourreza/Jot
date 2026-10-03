# Jot document icon

The `.jot` file icon uses a folded white document with the existing approved yellow/teal/blue Jot mark. The app and tray artwork are unchanged. A restrained gray outline keeps the document identifiable on both light and dark Explorer backgrounds; small sizes receive an optically adjusted stroke.

`NoteFileIcon.cs` generates native PNG-backed ICO frames at 16, 20, 24, 32, 48, 64, 128 and 256 pixels. Run `scripts/make-file-icon.ps1 -NoRestore` to regenerate `assets/jot-file.ico`, its 256px preview, and a light/dark actual-size sheet. This command does not launch the normal app or touch note data.

The installer’s optional `.jot` file association points to this icon. Preparing these assets does not change the running app or register associations on the development machine.
