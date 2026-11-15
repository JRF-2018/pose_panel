VrcPoseAliasesPanel {VERSION}
==============================

A small control panel that makes your VRChat avatar take poses you have registered by name (aliases).
It is an example of using OscPoser's control interface (the same one its MCP uses); OscPoser is what sends poses to VRChat.
You do not need this panel to use OscPoser (OscPoser works alone); it keeps your favorite poses named and ready.
It takes some effort to get started. Please read the README on GitHub:

  https://github.com/JRF-2018/pose_panel

* Requirements
- Windows 10 / 11
- OscPoser 0.4.0 or later (https://github.com/JRF-2018/osc_poser), with its prefab in the avatar
- VRChat

* Getting started
0. English users: first rename pose_aliases.en.txt to pose_aliases.txt (replace the Japanese example).
1. Extract OscPoser and put its OscPoser_<version> folder inside or next to this folder (found automatically).
2. Start VrcPoseAliasesPanel.exe (it opens OscPoser too). Set the language in ⚙ if needed;
   the panel opens OscPoser and the viewer in the same language.
3. Open the viewer (VrmPoseViewer) with ▣ and first drop a VRM on it.
4. For the example poses, get Necocoya's free pose set and emote set on Booth and drop the zips on the viewer's import screen.
     https://necocoya.booth.pm/items/1634088
     https://necocoya.booth.pm/items/1065089
5. Choose the target (VRChat profile number, usually 0) with 0/1/2, pick a pose from the menu, and send it with ➤.

* Aliases (pose_aliases.txt)
The panel's pose list is the file pose_aliases.txt in this folder.
  One per line:  name  anim  path-to.anim  [time]   (or  name  pose  saved-pose-name)
  A [group] line starts a submenu. The order is the order in the file.
Building your own pose_aliases.txt from your favorite poses is recommended.
Reorder etc. by editing the file directly (the viewer can open it in Notepad).
The viewer's Register / Edit dialogs also add, rename and group aliases one at a time.

* Profiles 0, 1, 2
With --profile=N, several VRChat clients run on one PC (each with its own account).
The panel's 0/1/2 are these numbers; their OSC ports are 9000, 9010, 9020. With one avatar, 0 is enough.
⏻ launches VRChat with the selected number.

* License
MIT License (LICENSE.txt). The viewer includes UniVRM and more (THIRD_PARTY_NOTICES.txt).
The example pose_aliases.txt (and pose_aliases.en.txt) only lists names and locations of Necocoya's assets, no animations.

JRF ( http://jrf.cocolog-nifty.com/statuses , X: @jion_rockford )
Mostly written by Claude Opus 5.5.
