using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

public class DemoIntegration : MonoBehaviour
{
    [SerializeField] private AudioController audioController;
    [SerializeField] private TextMeshProUGUI eventsText;
    [SerializeField] private TextMeshProUGUI conversationText;
    [SerializeField] private Button pushToTalkButton;
    [SerializeField] private Button connectButton;
    [SerializeField] private TextMeshProUGUI pushToTalkButtonText;
    [SerializeField] private TextMeshProUGUI connectButtonText;
    [SerializeField] private Button manualListeningButton;
    [SerializeField] private Button vadListeningButton;
    [SerializeField] private TextMeshProUGUI manualListeningButtonText;
    [SerializeField] private TextMeshProUGUI vadListeningButtonText;

    [SerializeField] private Image[] frequencyBars;
    [SerializeField] private Image[] aiFrequencyBars;

    int logCountLimit = 14;

    float maxFrequencyAmplitude = 4f;
    float aiMaxFrequencyAmplitude = 0.1f;

    bool isRecording = false;
    List<string> logMessages = new List<string>();
    List<string> conversationMessages = new List<string>();
    string currentConversationLine = "";


    float[] userBarAmplitudes;
    float[] aiBarAmplitudes;
    float barSmoothingSpeed = 5f;

    private void Start()
    {
        pushToTalkButton.onClick.AddListener(OnRecordButtonPressed);
        RealtimeAPIWrapper.OnWebSocketConnected += OnWebSocketConnected;
        RealtimeAPIWrapper.OnWebSocketClosed += OnWebSocketClosed;
        RealtimeAPIWrapper.OnSessionCreated += OnSessionCreated;
        RealtimeAPIWrapper.OnConversationItemCreated += OnConversationItemCreated;
        RealtimeAPIWrapper.OnResponseDone += OnResponseDone;
        RealtimeAPIWrapper.OnTranscriptReceived += OnTranscriptReceived;
        RealtimeAPIWrapper.OnResponseCreated += OnResponseCreated;

        AudioController.OnVADRecordingStarted += OnVADRecordingStarted;
        AudioController.OnVADRecordingEnded += OnVADRecordingEnded;

        manualListeningButton.onClick.AddListener(OnManualListeningMode);
        vadListeningButton.onClick.AddListener(OnVADListeningMode);

        UpdateListeningModeButtons();
        UpdateRecordButton();

        userBarAmplitudes = new float[frequencyBars.Length];
        aiBarAmplitudes = new float[aiFrequencyBars.Length];
    }

    private void Update()
    {
        if (audioController.listeningMode == ListeningMode.PushToTalk)
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
        UpdateFrequencyBars();
        UpdateAIFrequencyBars();
    }

    private void UpdateFrequencyBars()
    {
        if (frequencyBars == null || frequencyBars.Length == 0)
            return;

        if (!isRecording && audioController.listeningMode == ListeningMode.PushToTalk)
        {
            for (int i = 0; i < frequencyBars.Length; i++)
            {
                userBarAmplitudes[i] = Mathf.Lerp(userBarAmplitudes[i], 0f, Time.deltaTime * barSmoothingSpeed);
                frequencyBars[i].fillAmount = userBarAmplitudes[i];
            }
            return;
        }

        float[] spectrum = audioController.frequencyData;
        if (spectrum == null || spectrum.Length == 0)
        {
            for (int i = 0; i < frequencyBars.Length; i++)
            {
                userBarAmplitudes[i] = Mathf.Lerp(userBarAmplitudes[i], 0f, Time.deltaTime * barSmoothingSpeed);
                frequencyBars[i].fillAmount = userBarAmplitudes[i];
            }
            return;
        }

        float sampleRate = audioController.sampleRate;
        int fftSize = audioController.fftSampleSize;
        float nyquist = sampleRate / 2f;
        float freqPerBin = nyquist / fftSize;
        float[] freqBands = new float[] { 85f, 160f, 255f, 350f, 500f, 1000f, 2000f, 3000f, 4000f, nyquist };

        for (int i = 0; i < frequencyBars.Length; i++)
        {
            int startIndex = i == 0 ? 0 : Mathf.FloorToInt(freqBands[i - 1] / freqPerBin);
            int endIndex = Mathf.FloorToInt(freqBands[i] / freqPerBin);
            float sum = 0f;
            for (int j = startIndex; j < endIndex; j++)
            {
                sum += spectrum[j];
            }
            int sampleCount = endIndex - startIndex;
            float average = sampleCount > 0 ? sum / sampleCount : 0f;
            float amplitude = average * Mathf.Pow(2f, i);
            amplitude = Mathf.Clamp01(amplitude / maxFrequencyAmplitude);
            userBarAmplitudes[i] = Mathf.Lerp(userBarAmplitudes[i], amplitude, Time.deltaTime * barSmoothingSpeed);
            frequencyBars[i].fillAmount = userBarAmplitudes[i];
        }
    }

    private void UpdateAIFrequencyBars()
    {
        if (aiFrequencyBars == null || aiFrequencyBars.Length == 0)
            return;
        float[] spectrum = audioController.aiFrequencyData;
        if (spectrum == null || spectrum.Length == 0)
        {
            for (int i = 0; i < aiFrequencyBars.Length; i++)
            {
                aiBarAmplitudes[i] = Mathf.Lerp(aiBarAmplitudes[i], 0f, Time.deltaTime * barSmoothingSpeed);
                aiFrequencyBars[i].fillAmount = aiBarAmplitudes[i];
            }
            return;
        }

        float sampleRate = audioController.sampleRate;
        int fftSize = audioController.fftSampleSize;
        float nyquist = sampleRate / 2f;
        float freqPerBin = nyquist / fftSize;
        float[] freqBands = new float[] { 85f, 160f, 255f, 350f, 500f, 1000f, 2000f, 3000f, 4000f, nyquist };

        for (int i = 0; i < aiFrequencyBars.Length; i++)
        {
            int startIndex = i == 0 ? 0 : Mathf.FloorToInt(freqBands[i - 1] / freqPerBin);
            int endIndex = Mathf.FloorToInt(freqBands[i] / freqPerBin);
            float sum = 0f;
            for (int j = startIndex; j < endIndex; j++)
            {
                sum += spectrum[j];
            }
            int sampleCount = endIndex - startIndex;
            float average = sampleCount > 0 ? sum / sampleCount : 0f;
            float amplitude = average * Mathf.Pow(2f, i);
            amplitude = Mathf.Clamp01(amplitude / aiMaxFrequencyAmplitude);
            aiBarAmplitudes[i] = Mathf.Lerp(aiBarAmplitudes[i], amplitude, Time.deltaTime * barSmoothingSpeed);
            aiFrequencyBars[i].fillAmount = aiBarAmplitudes[i];
        }
    }

    private void OnRecordButtonPressed()
    {
        if (audioController.listeningMode == ListeningMode.PushToTalk)
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
    }

    private void StartRecording()
    {
        audioController.StartRecording();
        isRecording = true;
        AddLogMessage("recording...");
        UpdateRecordButton();
    }

    private void StopRecording()
    {
        audioController.StopRecording();
        isRecording = false;
        AddLogMessage("recording stopped. sending audio...");
        UpdateRecordButton();
    }

    private void OnVADRecordingStarted() => AddLogMessage("VAD recording started...");
    private void OnVADRecordingEnded() => AddLogMessage("VAD recording ended.");

    private void UpdateRecordButton()
    {
        if (audioController.listeningMode == ListeningMode.PushToTalk)
        {
            pushToTalkButton.interactable = true;
            if (isRecording)
            {
                pushToTalkButton.image.color = Color.red;
                pushToTalkButtonText.text = "release to send";
                pushToTalkButtonText.color = Color.white;
            }
            else
            {
                pushToTalkButton.image.color = new Color(236f / 255f, 236f / 255f, 241f / 255f);
                pushToTalkButtonText.text = "push to talk";
                pushToTalkButtonText.color = new Color(50f / 255f, 50f / 255f, 50f / 255f);
            }
        }
        else
        {
            pushToTalkButton.interactable = false;
            pushToTalkButton.image.color = Color.clear;
            pushToTalkButtonText.text = "";
        }
    }

    private void OnManualListeningMode()
    {
        AddLogMessage("Manual listening mode activated (push to talk / spacebar).");

        audioController.listeningMode = ListeningMode.PushToTalk;
        audioController.StopMicrophone();

        UpdateListeningModeButtons();
        UpdateRecordButton();
    }

    private void OnVADListeningMode()
    {
        AddLogMessage("VAD listening mode activated (super basic client-side vad, threshold-based).");

        audioController.listeningMode = ListeningMode.VAD;
        audioController.StartMicrophone();
        if (isRecording)
        {
            StopRecording();
        }

        UpdateListeningModeButtons();
        UpdateRecordButton();
    }

    private void UpdateListeningModeButtons()
    {
        if (audioController.listeningMode == ListeningMode.PushToTalk)
        {
            SetButtonActive(manualListeningButton, manualListeningButtonText);
            SetButtonInactive(vadListeningButton, vadListeningButtonText);
        }
        else if (audioController.listeningMode == ListeningMode.VAD)
        {
            SetButtonActive(vadListeningButton, vadListeningButtonText);
            SetButtonInactive(manualListeningButton, manualListeningButtonText);
        }
    }

    private void SetButtonActive(Button button, TextMeshProUGUI buttonText)
    {
        buttonText.color = Color.white;

        ColorBlock cb = button.colors;
        cb.normalColor = cb.selectedColor = new Color(15f / 255f, 15f / 255f, 15f / 255f);
        cb.highlightedColor = cb.pressedColor = new Color(64f / 255f, 64f / 255f, 64f / 255f);
        button.colors = cb;
    }

    private void SetButtonInactive(Button button, TextMeshProUGUI buttonText)
    {
        buttonText.color = new Color(50f / 255f, 50f / 255f, 50f / 255f);

        ColorBlock cb = button.colors;
        cb.normalColor = cb.selectedColor = Color.clear;
        cb.highlightedColor = cb.pressedColor = new Color(216f / 255f, 216f / 255f, 216f / 255f);
        button.colors = cb;
    }


    private void AddLogMessage(string message)
    {
        if (logMessages.Count >= logCountLimit) logMessages.RemoveAt(0);

        string timestamp = System.DateTime.Now.ToString("HH:mm:ss");

        logMessages.Add($"{timestamp}\t{message}");
        UpdateEventsText();
    }

    private void UpdateEventsText()
    {
        eventsText.text = "";
        for (int i = 0; i < logMessages.Count; i++)
        {
            float alpha = Mathf.Lerp(0.2f, 1.0f, (float)(i + 1) / logMessages.Count);
            string logWithAlpha = $"<color=#{ColorUtility.ToHtmlStringRGBA(new Color(0, 0, 0, alpha))}>{logMessages[i]}</color>";
            eventsText.text += logWithAlpha + "\n";
        }
    }

    private void OnWebSocketConnected()
    {
        AddLogMessage("connection established.");
        connectButtonText.text = "disconnect";
        connectButtonText.color = new Color(50f / 255f, 50f / 255f, 50f / 255f);

        ColorBlock cb = connectButton.colors;
        cb.normalColor = cb.selectedColor = new Color(236f / 255f, 236f / 255f, 241f / 255f);
        cb.highlightedColor = cb.pressedColor = new Color(216f / 255f, 216f / 255f, 216f / 255f);
        connectButton.colors = cb;
    }

    private void OnWebSocketClosed()
    {
        AddLogMessage("connection closed.");
        connectButtonText.text = "connect";
        connectButtonText.color = Color.white;

        ColorBlock cb = connectButton.colors;
        cb.normalColor = cb.selectedColor = new Color(15f / 255f, 15f / 255f, 15f / 255f);
        cb.highlightedColor = cb.pressedColor = new Color(64f / 255f, 64f / 255f, 64f / 255f);
        if (connectButton) connectButton.colors = cb;
    }

    private void OnSessionCreated()
    {
        AddLogMessage("session created.");
    }

    private void OnConversationItemCreated()
    {
        AddLogMessage("conversation item created.");

        if (!string.IsNullOrEmpty(currentConversationLine))
        {
            if (conversationMessages.Count >= logCountLimit) conversationMessages.RemoveAt(0);
            conversationMessages.Add(currentConversationLine);
        }

        currentConversationLine = "";
        UpdateConversationText();
    }


    private void OnResponseDone()
    {
        AddLogMessage("response done.");
    }

    private void OnTranscriptReceived(string transcriptPart)
    {
        if (string.IsNullOrEmpty(currentConversationLine))
        {
            string timestamp = System.DateTime.Now.ToString("HH:mm:ss");
            currentConversationLine = $"{timestamp}\t";
        }

        currentConversationLine += transcriptPart;

        UpdateConversationTextInPlace();
    }


    private void UpdateConversationTextInPlace()
    {
        conversationText.text = "";

        for (int i = 0; i < conversationMessages.Count; i++)
        {
            float alpha = Mathf.Lerp(0.2f, 1.0f, (float)(i + 1) / conversationMessages.Count);
            string messageWithAlpha = $"<color=#{ColorUtility.ToHtmlStringRGBA(new Color(0, 0, 0, alpha))}>{conversationMessages[i]}</color>";
            conversationText.text += messageWithAlpha + "\n";
        }

        conversationText.text += $"<color=#{ColorUtility.ToHtmlStringRGBA(new Color(0, 0, 0, 1.0f))}>{currentConversationLine}</color>";
    }

    private void UpdateConversationText()
    {
        conversationText.text = "";

        for (int i = 0; i < conversationMessages.Count; i++)
        {
            float alpha = Mathf.Lerp(0.2f, 1.0f, (float)(i + 1) / conversationMessages.Count);
            string messageWithAlpha = $"<color=#{ColorUtility.ToHtmlStringRGBA(new Color(0, 0, 0, alpha))}>{conversationMessages[i]}</color>";
            conversationText.text += messageWithAlpha + "\n";
        }
    }

    private void OnResponseCreated()
    {
        AddLogMessage("response created.");
    }
}
