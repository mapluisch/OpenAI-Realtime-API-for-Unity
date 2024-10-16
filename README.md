<div align="center">
  <h1 align="center">OpenAI Realtime API Integration for Unity</h1  >
  <img src="https://martinpluisch.com/openai-realtime-api">
  <p><em>Implementation of OpenAI's Realtime API in Unity - record microphone audio and receive real-time low-latency responses.</em></p>
</div>

---

## Overview
On October 1st, [OpenAI introduced their Realtime API](https://openai.com/index/introducing-the-realtime-api/).


This (WIP) project integrates the API into a Unity application, allowing users to build low-latency, multi-modal conversational apps that support both text and audio input/output, as well as function calling (via [OpenAI Realtime API documentation](https://platform.openai.com/docs/guides/realtime)).

Specifically, this package allows you to integrate real-time low-latency voice conversations with OpenAI's TTS/STT conversational models (via push-to-talk or VAD).
I've integrated transcriptions, natural speech interruption handling, and client-side VAD (as per [Whisper's VAD](https://raw.githubusercontent.com/Macoron/whisper.unity/275406258aca21fe7753cf0724a65f06fd464eea/Packages/com.whisper.unity/Runtime/Utils/AudioUtils.cs)).

Tested with Unity version 2022.3.45f1 (on macOS, but it should work on every platform that supports `Newtonsoft.Json`).

## Demos
> disclaimer, I liked the UI used by OpenAI [in this project](https://github.com/openai/openai-realtime-console/tree/main), as you can see :) 
### Standard conversation
https://github.com/user-attachments/assets/73b69bd7-dae8-4d49-8f7f-02fa32d9a955

### Interruption example
https://github.com/user-attachments/assets/88fd83eb-e285-488d-8308-a40a48a7307f


### Setup
1. Download the latest release `.unitypackage`.
2. Import it into your own project, e.g., via `Assets > Import Package`.
3. Go to Package Manager, click on the `+`, then `Add package by name`, and add `com.unity.nuget.newtonsoft-json`.
4. Either open the `DemoScene` scene or add the necessary Prefabs to your own scene.

### Using OpenAI Realtime API in your own scene
1. Add the `RealtimeAPI` Prefab to your scene.
2. Add your OpenAI API key to the `RealtimeAPI` Prefab.
3. Optional: Add the `DemoIntegration` Prefab to a Canvas within your scene or open up the `DemoScene` to see an integration example.
4. Reference the `AudioController`, nested inside the `RealtimeAPI` Prefab, and call `audioController.StartRecording()` and `audioController.StopRecording()` to start resp. stop a microphone recording (as push-to-talk).
5. Bind a button or key to both actions - within my `DemoIntegration`, the spacebar can also be used as push-to-talk key.
6. The audio is automatically recorded, converted to PCM16 and Base64, sent via the WebSocket connection to the API, and the received chunks and written transcript are handled and served via events.

### Action Events
The `RealtimeAPIWrapper` component triggers various **Action** events, enabling you to handle key stages of the real-time session. Below is a list of available Action events:

#### Available Action Events:
- `OnWebSocketConnected`: Triggered when the WebSocket connection is successfully established.
- `OnWebSocketClosed`: Triggered when the WebSocket connection is closed.
- `OnSessionCreated`: Triggered when a session is successfully created.
- `OnConversationItemCreated`: Triggered when a conversation item is created.
- `OnResponseDone`: Triggered when the response is fully processed.
- `OnTranscriptReceived`: Triggered each time a transcript chunk is received (string argument containing the transcript part).
- `OnResponseCreated`: Triggered when a new response is initiated.
- `OnResponseAudioDone`: Triggered when the audio portion of the response is done.
- `OnResponseAudioTranscriptDone`: Triggered when the audio transcript portion of the response is done.
- `OnResponseContentPartDone`: Triggered when the content part of the response is done.
- `OnResponseOutputItemDone`: Triggered when an output item of the response is done.
- `OnResponseOutputItemAdded`: Triggered when a new output item is added to the response.
- `OnResponseContentPartAdded`: Triggered when a new content part is added to the response.
- `OnRateLimitsUpdated`: Triggered when rate limits are updated by the API.

## ToDo
This project is still very much a work in progress. I'd like to extend it in the coming days to add:

- [ ] Server-Side VAD (no guarantees, though)
- [ ] Text / chat input -> TTS + transcription output
- [x] Client-Side VAD
- [x] Interruption handling


## Disclaimer
This project serves as an example of integrating OpenAI's Realtime API with Unity. It is a prototype, so feel free to extend its functionality or submit a PR for improvements.

