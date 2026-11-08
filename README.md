# Claude Switcher

A tiny Windows app for switching the **Claude desktop app** between accounts with one click.

- One button per account, plus **+ Add account**
- Light and dark mode (follows Windows; toggle with the sun/moon button)
- A single ~30 KB `.exe` with nothing to install, built from one C# file

> Unofficial community tool. Not affiliated with or endorsed by Anthropic.

## Use it

1. Open `Claude Switcher.exe`.
2. Name the account you're signed in to now, for example *Work*.
3. Click **+ Add account** and give the account a name. Claude reopens on its sign-in screen, where you log in with that account.
4. From then on, **click an account to switch to it**. Hover over an account and click **⋯** to rename or delete it.

Switching closes Claude completely, including any chats or Claude Code sessions running in it.

## How it works

The Claude desktop app (Microsoft Store version) keeps everything in one folder:
`%LOCALAPPDATA%\Claude-Data\claude_<id>`. That includes your sign-in, settings and local sessions.

Claude Switcher saves one copy of that folder per account in `%LOCALAPPDATA%\ClaudeSwitcher\profiles\<name>`. A switch works like this:

1. Closes Claude.
2. Renames the live folder to `profiles\<current account>`.
3. Renames `profiles\<target account>` back into place. For a new account the folder is left out, so Claude starts on a fresh sign-in screen.
4. Starts Claude.

Renames on the same drive are instant, so nothing is copied, uploaded or sent anywhere. If you open the exe from inside Claude's data folder, it first copies itself to `%LOCALAPPDATA%\ClaudeSwitcher` and adds a Desktop shortcut, because that folder gets moved during a switch.

## Build

You need Windows 10 or 11. The C# compiler for .NET Framework 4 ships with Windows, so there's nothing else to install.

```bat
build.cmd
```

To try it without touching your real Claude data:

```bat
"Claude Switcher.exe" --test --data C:\temp\fake-data --store C:\temp\fake-store\profiles
```

## Limitations

- Supports only the Microsoft Store / MSIX version of Claude desktop.
- Each account keeps its own settings and history. Nothing is shared between accounts.

## License

MIT. See [LICENSE](LICENSE).
