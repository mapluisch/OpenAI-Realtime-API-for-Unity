using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

public class DemoIntegration : MonoBehaviour
{
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI transcriptText;
    public Button recordButton;
    public TextMeshProUGUI recordButtonText;
    public AudioController audioController;

    private bool isRecording = false;
    private List<string> logMessages = new List<string>();

    private void Start()
    {
        recordButton.onClick.AddListener(OnRecordButtonPressed);
        RealtimeAPIWrapper.OnWebSocketConnected += OnWebSocketConnected;
        RealtimeAPIWrapper.OnWebSocketClosed += OnWebSocketClosed;
        RealtimeAPIWrapper.OnSessionCreated += OnSessionCreated;
        RealtimeAPIWrapper.OnConversationItemCreated += OnConversationItemCreated;
        RealtimeAPIWrapper.OnResponseDone += OnResponseDone;
        RealtimeAPIWrapper.OnTranscriptReceived += OnTranscriptReceived;
        RealtimeAPIWrapper.OnResponseCreated += OnResponseCreated;
        UpdateRecordButton();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && !isRecording)
        {
            StartRecording();
        }

        if (Input.GetKeyUp(KeyCode.Space) && isRecording)
        {
            StopRecording();
        }
    }

    private void OnRecordButtonPressed()
    {
        if (isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    private void StartRecording()
    {
        audioController.StartRecording();
        isRecording = true;
        AddLogMessage("[Recording... Release 'Space' or press the button to stop.]");
        UpdateRecordButton();
    }

    private void StopRecording()
    {
        audioController.StopRecording();
        isRecording = false;
        AddLogMessage("[Recording stopped. Sending audio...]");
        UpdateRecordButton();
    }

    private void UpdateRecordButton()
    {
        if (isRecording)
        {
            recordButton.image.color = Color.red;
            recordButtonText.text = "[recording]";
        }
        else
        {
            recordButton.image.color = Color.green;
            recordButtonText.text = "[record]";
        }
    }

    private void AddLogMessage(string message)
    {
        if (logMessages.Count >= 5)
        {
            logMessages.RemoveAt(0);
        }

        logMessages.Add(message);
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        statusText.text = "";

        for (int i = 0; i < logMessages.Count; i++)
        {
            float alpha = Mathf.Lerp(0.2f, 1.0f, (float)(i + 1) / logMessages.Count);
            string logWithAlpha = $"<color=#{ColorUtility.ToHtmlStringRGBA(new Color(0, 0, 0, alpha))}>{logMessages[i]}</color>";
            statusText.text += logWithAlpha + "\n";
        }
    }

    private void OnWebSocketConnected()
    {
        AddLogMessage("[WebSocket connected]");
    }

    private void OnWebSocketClosed()
    {
        AddLogMessage("[WebSocket closed]");
    }

    private void OnSessionCreated()
    {
        AddLogMessage("[Session created]");
    }

    private void OnConversationItemCreated()
    {
        transcriptText.text = "";
        AddLogMessage("[Conversation item created]");
    }

    private void OnResponseDone()
    {
        AddLogMessage("[Response done]");
    }

    private void OnTranscriptReceived(string transcriptPart)
    {
        transcriptText.text += transcriptPart;
    }

    private void OnResponseCreated()
    {
        AddLogMessage("[Response created]");
    }
}
