using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using UnityEngine;
using Newtonsoft.Json.Linq;

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

    private async void Start()
    {
        ws = new ClientWebSocket();
        await ConnectWebSocket();
        audioController.AudioRecorded += SendAudioToAPI;
    }

    private async Task ConnectWebSocket()
    {
        Debug.Log("Connecting to WebSocket...");

        try
        {
            var uri = new Uri("wss://api.openai.com/v1/realtime?model=gpt-4o-realtime-preview-2024-10-01");
            ws.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
            ws.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");

            await ws.ConnectAsync(uri, CancellationToken.None);

            Debug.Log("WebSocket connection established.");
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

            Debug.Log("Sent response.cancel event.");
            OnResponseCancelled?.Invoke();
            isResponseInProgress = false;
        }
    }


    private async void SendAudioToAPI(string base64AudioData)
    {
        if (isResponseInProgress)
        {
            SendCancelEvent();
        }

        if (ws.State == WebSocketState.Open)
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

            Debug.Log("Audio and response request sent to API.");
        }
        else
        {
            Debug.LogError("WebSocket is closed. Audio could not be sent.");
        }
    }

    private async Task ReceiveMessages()
    {
        var buffer = new byte[1024 * 256]; //256kb - feel free to tinker around here

        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                string fullMessage = messageBuffer.ToString();

                if (!string.IsNullOrEmpty(fullMessage.Trim()))
                {
                    try
                    {
                        JObject eventMessage = JObject.Parse(fullMessage);

                        string messageType = eventMessage["type"]?.ToString();

                        if (messageType == "response.audio.delta")
                        {
                            string base64AudioData = eventMessage["delta"]?.ToString();
                            if (!string.IsNullOrEmpty(base64AudioData))
                            {
                                byte[] pcmAudioData = Convert.FromBase64String(base64AudioData);
                                audioController.EnqueueAudioData(pcmAudioData);
                            }
                        }
                        else if (messageType == "response.audio_transcript.delta")
                        {
                            string transcriptPart = eventMessage["delta"]?.ToString();
                            if (!string.IsNullOrEmpty(transcriptPart))
                            {
                                transcriptBuffer.Append(transcriptPart);
                                OnTranscriptReceived?.Invoke(transcriptPart);
                            }
                        }
                        else if (messageType == "conversation.item.created")
                        {
                            OnConversationItemCreated?.Invoke();
                        }
                        else if (messageType == "response.done")
                        {
                            if (!audioController.IsAudioPlaying())
                            {
                                isResponseInProgress = false;
                            }
                            OnResponseDone?.Invoke();
                        }
                        else if (messageType == "response.created")
                        {
                            transcriptBuffer.Clear();
                            isResponseInProgress = true;
                            OnResponseCreated?.Invoke();
                        }
                        else if (messageType == "session.created")
                        {
                            OnSessionCreated?.Invoke();
                        }
                        else if (messageType == "response.audio.done")
                        {
                            OnResponseAudioDone?.Invoke();
                        }
                        else if (messageType == "response.audio_transcript.done")
                        {
                            OnResponseAudioTranscriptDone?.Invoke();
                        }
                        else if (messageType == "response.content_part.done")
                        {
                            OnResponseContentPartDone?.Invoke();
                        }
                        else if (messageType == "response.output_item.done")
                        {
                            OnResponseOutputItemDone?.Invoke();
                        }
                        else if (messageType == "response.output_item.added")
                        {
                            OnResponseOutputItemAdded?.Invoke();
                        }
                        else if (messageType == "response.content_part.added")
                        {
                            OnResponseContentPartAdded?.Invoke();
                        }
                        else if (messageType == "rate_limits.updated")
                        {
                            OnRateLimitsUpdated?.Invoke();
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

                messageBuffer.Clear();
            }
        }
    }


    private async void OnApplicationQuit()
    {
        if (ws.State == WebSocketState.Open)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by user", CancellationToken.None);
            ws.Dispose();
            OnWebSocketClosed?.Invoke();
        }
    }
}
