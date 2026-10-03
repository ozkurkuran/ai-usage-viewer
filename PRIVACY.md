# Privacy policy

Effective 3 October 2026 · Applies to Ai UsageNest for Windows, including the
Microsoft Store package. [Türkçe özet aşağıda.](#gizlilik-politikası-türkçe)

Ai UsageNest is published by **Mikrofab** and developed as an open-source, local
application. Mikrofab does not operate a server for the app and does not receive
any information from it. There is no app account, telemetry, analytics,
advertising or crash reporting.

## What the app reads on your device

- **Claude Code and Codex usage records**: JSONL files in the profile folders you
  select, by default `%USERPROFILE%\.claude` and `%USERPROFILE%\.codex` (or
  `CLAUDE_CONFIG_DIR` / `CODEX_HOME`). The app keeps token counts, timestamps,
  model IDs and project/session identifiers. It does not store conversation text
  or source code.
- **Claude Code sign-in**: to show subscription quota, the app reads the access
  token in the selected profile's `.credentials.json` for each request. It does not
  store or change that token.
- **Codex sign-in**: the app starts the locally installed `codex app-server` and
  asks it for account quota. Codex uses its own credentials; the app does not read
  Codex's credential file.

## What the app stores

The optional Windows 11 Widgets board card displays a local summary of today's
tokens, estimated API equivalent, account labels and quota/balance information.
The app writes this display-only summary to `windows-widget.json` in its data
folder and passes the card content to the Windows widget host. It does not pass
credentials, conversations, project paths or session identifiers to the widget.
Opening the widget board can start the app's normal collector in the background;
the app remains available in the notification area until you exit it. Windows
Widgets is a Microsoft system component with its own privacy settings and policy.

Settings, usage history, quota observations and notification state are stored in
the app's data folder (`%LOCALAPPDATA%\AiUsageViewer`). In the Microsoft Store
version, Windows keeps this folder inside the app's package storage and removes it
when the app is uninstalled. OpenRouter API keys you enter are encrypted with
Windows DPAPI for your Windows user account. Exports are written only to the file
you choose.

## Network connections

The app connects only to the services you configure, directly from your device:

| Service | Destination | Sent |
|---|---|---|
| Claude subscription quota | `api.anthropic.com` | Your Claude Code access token |
| Codex subscription quota | Local `codex app-server` process, which contacts OpenAI | Quota request only |
| OpenRouter | `openrouter.ai` | Your OpenRouter API key |

Network requests use HTTPS. These services receive the requests under their own
privacy policies. The app does not send your data to Mikrofab or to anyone else,
and does not sell or share it.

## Your choices

All of this information stays on your device, so you can see and control it
directly: you can remove accounts, sources and API keys in Settings, delete the
data folder while the app is closed, or uninstall the app. Windows startup and
notifications are off until you turn them on. "Try with sample data" opens a
separate window with generated example records stored in its own folder; it does
not read your files or connect to any service.

## Children

The app is a developer tool and is not directed at children.

## Changes and contact

Changes to this policy are published in this repository with a new effective date.
Publisher: Mikrofab. Privacy and support questions: [app@mikrofab.com](mailto:app@mikrofab.com)
or [GitHub issues](https://github.com/ozkurkuran/ai-usage-viewer/issues).
Security reports: [private vulnerability reporting](https://github.com/ozkurkuran/ai-usage-viewer/security/advisories/new).

## Gizlilik politikası (Türkçe)

Ai UsageNest, **Mikrofab** tarafından yayımlanan, yerel çalışan açık kaynaklı bir
uygulamadır. Mikrofab uygulama için bir sunucu işletmez ve uygulamadan hiçbir
bilgi almaz. Uygulama hesabı, telemetri, analitik, reklam veya hata raporu
gönderimi yoktur.

- Seçtiğin Claude Code/Codex profil klasörlerindeki JSONL kayıtlarından token
  sayıları, zaman, model ve proje/oturum kimlikleri okunur. Sohbet metni veya
  kaynak kod saklanmaz.
- Claude kotası için seçilen profildeki `.credentials.json` içindeki erişim
  anahtarı her istekte okunur ve yalnız `api.anthropic.com` adresine gönderilir;
  saklanmaz veya değiştirilmez. Codex kotası yerel `codex app-server` süreciyle
  alınır; Codex kendi kimlik bilgisini kullanır. OpenRouter anahtarı Windows DPAPI
  ile şifrelenir ve yalnız `openrouter.ai` adresine gönderilir.
- Veriler `%LOCALAPPDATA%\AiUsageViewer` klasöründe tutulur. Microsoft Store
  sürümünde Windows bu klasörü uygulama paketinin alanında tutar ve uygulama
  kaldırıldığında siler.
- Veriler Mikrofab'a veya üçüncü kişilere gönderilmez, satılmaz, paylaşılmaz.
- "Örnek veriyle dene" ayrı klasörde üretilmiş örnek kayıtlar gösterir; dosyalarını
  okumaz ve hiçbir servise bağlanmaz.
- Sorular ve destek: [app@mikrofab.com](mailto:app@mikrofab.com).
