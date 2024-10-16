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
    [SerializeField] string apiKey = "YOUR_API_KEY";
    public AudioPlayer audioPlayer;
    public AudioRecorder audioRecorder;
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

    private void Start() => AudioRecorder.OnAudioRecorded += SendAudioToAPI;
    private void OnApplicationQuit() => DisposeWebSocket();


    /// <summary>
    /// connects or disconnects websocket when button is pressed
    /// </summary>
    public async void ConnectWebSocketButton()
    {
        if (ws != null) DisposeWebSocket();
        else
        {
            ws = new ClientWebSocket();
            await ConnectWebSocket();
        }
    }

    /// <summary>
    /// establishes websocket connection to the api
    /// </summary>
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
            Debug.LogError("websocket connection failed: " + e.Message);
        }
    }

    /// <summary>
    /// sends a cancel event to api if response is in progress
    /// </summary>
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

    /// <summary>
    /// sends recorded audio to the api
    /// </summary>
    private async void SendAudioToAPI(string base64AudioData)
    {
        if (isResponseInProgress)
            SendCancelEvent();

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

    /// <summary>
    /// receives messages from websocket and handles them
    /// </summary>
    private async Task ReceiveMessages()
    {
        var buffer = new byte[1024 * 128];
        var messageHandlers = GetMessageHandlers();

        while (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (ws.State == WebSocketState.CloseReceived)
            {
                Debug.Log("websocket close received, disposing current ws instance.");
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

                        if (messageHandlers.TryGetValue(messageType, out var handler)) handler(eventMessage);

                        else Debug.Log("unhandled message type: " + messageType);

                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("error parsing json: " + ex.Message);
                    }
                }
            }
        }
    }

    /// <summary>
    /// returns dictionary of message handlers for different message types
    /// </summary>
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

    /// <summary>
    /// handles incoming audio delta messages from api
    /// </summary>
    private void HandleAudioDelta(JObject eventMessage)
    {
        string base64AudioData = eventMessage["delta"]?.ToString();
        if (!string.IsNullOrEmpty(base64AudioData))
        {
            byte[] pcmAudioData = Convert.FromBase64String(base64AudioData);
            audioPlayer.EnqueueAudioData(pcmAudioData);
        }
    }

    /// <summary>
    /// handles incoming transcript delta messages from api
    /// </summary>
    private void HandleTranscriptDelta(JObject eventMessage)
    {
        string transcriptPart = eventMessage["delta"]?.ToString();
        if (!string.IsNullOrEmpty(transcriptPart))
        {
            transcriptBuffer.Append(transcriptPart);
            OnTranscriptReceived?.Invoke(transcriptPart);
        }
    }

    /// <summary>
    /// handles response.done message - checks if audio is still playing
    /// </summary>
    private void HandleResponseDone(JObject eventMessage)
    {
        if (!audioPlayer.IsAudioPlaying())
        {
            isResponseInProgress = false;
        }
        OnResponseDone?.Invoke();
    }

    /// <summary>
    /// handles response.created message - resets transcript buffer
    /// </summary>
    private void HandleResponseCreated(JObject eventMessage)
    {
        transcriptBuffer.Clear();
        isResponseInProgress = true;
        OnResponseCreated?.Invoke();
    }

    /// <summary>
    /// handles error messages from api
    /// </summary>
    private void HandleError(JObject eventMessage)
    {
        string errorMessage = eventMessage["error"]?["message"]?.ToString();
        if (!string.IsNullOrEmpty(errorMessage))
        {
            Debug.LogError("openai error: " + errorMessage);
        }
    }

    /// <summary>
    /// disposes the websocket connection
    /// </summary>
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

}
