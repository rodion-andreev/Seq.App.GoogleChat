# Seq.App.GoogleChat [![NuGet](https://img.shields.io/nuget/v/Seq.App.GoogleChat.svg?style=flat-square)](https://www.nuget.org/packages/Seq.App.GoogleChat/)

An app for [Seq](https://datalust.co/seq) that posts events and alert notifications to a [Google Chat](https://chat.google.com) space as cards: level, message, a link back to the event in Seq, and (optionally) the exception and properties.

## Getting started

1. **Create the webhook.** In Google Chat (browser, not the mobile app) open the space, click the arrow next to its name > _Apps & integrations_ > _Webhooks_ > _Add webhook_, give it a name, save, then _More_ > _Copy link_. If the button is missing, your Workspace admin does not allow incoming webhooks.
2. **Install the app.** In Seq: _Settings_ > _Apps_ > _Install from NuGet_, package id `Seq.App.GoogleChat`.
3. **Add an instance.** _Settings_ > _Apps_ > _Add instance_ next to _Google Chat Notifier_, paste the webhook URL.
4. **Choose what it receives.** Either a signal stream (_Stream incoming events_ with a signal such as _Errors_), or, preferably, use the instance as an **alert notification channel**: Seq alerts aggregate, so a burst of errors becomes one message.
5. **Set Seq's canonical URI** (`api.canonicalUri`; `SEQ_API_CANONICALURI` for the Docker image) to the address people open Seq at, otherwise "View this event in Seq" links point to `localhost`.

## Settings

| Setting | Default | Notes |
| --- | --- | --- |
| Webhook URL | — | Required, `https://` only. It carries the space's key and token, so it is stored and shown as a secret. |
| Suppression time (minutes) | 0 | After an event type is sent, the same type is not sent again for this long. |
| Exclude optional attachments | off | Send only level, message and link — no exception, no properties. |
| Proxy Server | — | e.g. `http://proxy:3128`, with the Seq service account's default credentials. |
| Maximum property length | — | Truncate each property value. Values are always capped at 4,000 characters. |
| Included properties | all | Comma-separated property names to attach. |

## Delivery behaviour

- Google Chat accepts **one request per second per space**, shared by all webhooks in it. The app spaces its posts accordingly and retries `429` and `5xx` answers and network failures (up to 4 attempts, honouring `Retry-After`, 15 s timeout per request). Other errors (e.g. `400`, `404` for a deleted webhook) fail at once.
- A message that cannot be delivered is reported in the app instance's diagnostics in Seq. The error never contains the webhook URL.
- Messages stay under the Google Chat size limit (32,000 bytes): long values are truncated and properties that do not fit are counted as "not shown".
- Event text is HTML-escaped, so `<`, `>` and `&` in messages or properties render literally.

## Requirements

- A Seq version whose app host runs on .NET 8 or later.
- Outbound HTTPS from the Seq host to `chat.googleapis.com` (directly or through the proxy setting).
- Everything the app sends leaves your network for Google: use _Exclude optional attachments_ or _Included properties_ when events can carry personal or confidential data.

Consult the Seq documentation for more about [installing Seq apps](https://docs.datalust.co/docs/installing-seq-apps).
