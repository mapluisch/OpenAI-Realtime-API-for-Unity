using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using UnityEngine;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

public class RealtimeAPIWrapper : MonoBehaviour
{
    private ClientWebSocket ws;
    public string apiKey = "YOUR_API_KEY";
    public AudioController audioController;
    private StringBuilder messageBuffer = new StringBuilder();
    private StringBuilder transcriptBuffer = new StringBuilder();
    private bool isResponseInProgress = false;
    public static event Action OnWebSocketConnected;
    public static event Action OnWebSocketClosed;
    public static event Action OnSessionCreated;
    public static event Action OnConversationItemCreated;
    public static event Action OnResponseDone;
    public static event Action<string> OnTranscriptReceived;
    public static event Action OnResponseCreated;
    public static event Action OnResponseAudioDone;
    public static event Action OnResponseAudioTranscriptDone;
    public static event Action OnResponseContentPartDone;
    public static event Action OnResponseOutputItemDone;
    public static event Action OnRateLimitsUpdated;
    public static event Action OnResponseOutputItemAdded;
    public static event Action OnResponseContentPartAdded;
    public static event Action OnResponseCancelled;

    private void Start() => AudioController.OnAudioRecorded += SendAudioToAPI;

    public async void ConnectWebSocketButton()
    {
        if (ws != null) DisposeWebSocket();
        else
        {
            ws = new ClientWebSocket();
            await ConnectWebSocket();
        }
    }

    private async Task ConnectWebSocket()
    {
        try
        {
            var uri = new Uri("wss://api.openai.com/v1/realtime?model=gpt-4o-realtime-preview-2024-10-01");
            ws.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
            ws.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");
            await ws.ConnectAsync(uri, CancellationToken.None);
            OnWebSocketConnected?.Invoke();
            _ = ReceiveMessages();
        }
        catch (Exception e)
        {
            Debug.LogError("WebSocket connection failed: " + e.Message);
        }
    }

    private async void SendCancelEvent()
    {
        if (ws.State == WebSocketState.Open && isResponseInProgress)
        {
            var cancelMessage = new
            {
                type = "response.cancel"
            };
            string jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(cancelMessage);
            byte[] messageBytes = Encoding.UTF8.GetBytes(jsonString);
            await ws.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
            OnResponseCancelled?.Invoke();
            isResponseInProgress = false;
        }
    }

    private async void SendAudioToAPI(string base64AudioData)
    {
        if (isResponseInProgress) SendCancelEvent();

        if (ws != null && ws.State == WebSocketState.Open)
        {
            var eventMessage = new
            {
                type = "conversation.item.create",
                item = new
                {
                    type = "message",
                    role = "user",
                    content = new[]
                    {
                        new { type = "input_audio", audio = base64AudioData }
                    }
                }
            };
            string jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(eventMessage);
            byte[] messageBytes = Encoding.UTF8.GetBytes(jsonString);
            await ws.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);

            var responseMessage = new
            {
                type = "response.create",
                response = new
                {
                    modalities = new[] { "audio", "text" },
                    instructions = "Please provide a transcript."
                }
            };
            string responseJson = Newtonsoft.Json.JsonConvert.SerializeObject(responseMessage);
            byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);
            await ws.SendAsync(new ArraySegment<byte>(responseBytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }

    private async Task ReceiveMessages()
    {
        var buffer = new byte[1024 * 256];
        var messageHandlers = GetMessageHandlers();

        while (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (ws.State == WebSocketState.CloseReceived)
            {
                Debug.Log("WebSocket close received, disposing current WS instance.");
                DisposeWebSocket();
                return;
            }

            if (result.EndOfMessage)
            {
                string fullMessage = messageBuffer.ToString();
                messageBuffer.Clear();

                if (!string.IsNullOrEmpty(fullMessage.Trim()))
                {
                    try
                    {
                        JObject eventMessage = JObject.Parse(fullMessage);
                        string messageType = eventMessage["type"]?.ToString();

                        if (messageHandlers.TryGetValue(messageType, out var handler))
                        {
                            handler(eventMessage);
                        }
                        else
                        {
                            Debug.Log("Unhandled message type: " + messageType);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("Error parsing JSON: " + ex.Message);
                    }
                }
            }
        }
    }

    private Dictionary<string, Action<JObject>> GetMessageHandlers()
    {
        return new Dictionary<string, Action<JObject>>
        {
            { "response.audio.delta", HandleAudioDelta },
            { "response.audio_transcript.delta", HandleTranscriptDelta },
            { "conversation.item.created", _ => OnConversationItemCreated?.Invoke() },
            { "response.done", HandleResponseDone },
            { "response.created", HandleResponseCreated },
            { "session.created", _ => OnSessionCreated?.Invoke() },
            { "response.audio.done", _ => OnResponseAudioDone?.Invoke() },
            { "response.audio_transcript.done", _ => OnResponseAudioTranscriptDone?.Invoke() },
            { "response.content_part.done", _ => OnResponseContentPartDone?.Invoke() },
            { "response.output_item.done", _ => OnResponseOutputItemDone?.Invoke() },
            { "response.output_item.added", _ => OnResponseOutputItemAdded?.Invoke() },
            { "response.content_part.added", _ => OnResponseContentPartAdded?.Invoke() },
            { "rate_limits.updated", _ => OnRateLimitsUpdated?.Invoke() },
            { "error", HandleError }
        };
    }

    private void HandleAudioDelta(JObject eventMessage)
    {
        string base64AudioData = eventMessage["delta"]?.ToString();
        if (!string.IsNullOrEmpty(base64AudioData))
        {
            byte[] pcmAudioData = Convert.FromBase64String(base64AudioData);
            audioController.EnqueueAudioData(pcmAudioData);
        }
    }

    private void HandleTranscriptDelta(JObject eventMessage)
    {
        string transcriptPart = eventMessage["delta"]?.ToString();
        if (!string.IsNullOrEmpty(transcriptPart))
        {
            transcriptBuffer.Append(transcriptPart);
            OnTranscriptReceived?.Invoke(transcriptPart);
        }
    }

    private void HandleResponseDone(JObject eventMessage)
    {
        if (!audioController.IsAudioPlaying())
        {
            isResponseInProgress = false;
        }
        OnResponseDone?.Invoke();
    }

    private void HandleResponseCreated(JObject eventMessage)
    {
        transcriptBuffer.Clear();
        isResponseInProgress = true;
        OnResponseCreated?.Invoke();
    }

    private void HandleError(JObject eventMessage)
    {
        string errorMessage = eventMessage["error"]?["message"]?.ToString();
        if (!string.IsNullOrEmpty(errorMessage))
        {
            Debug.LogError("OpenAI error: " + errorMessage);
        }
    }

    private async void DisposeWebSocket()
    {
        if (ws != null && (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived))
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by user", CancellationToken.None);
            ws.Dispose();
            ws = null;
            OnWebSocketClosed?.Invoke();
        }
    }

    private void OnApplicationQuit() => DisposeWebSocket();
}
