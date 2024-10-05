using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

public class DemoIntegration : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI eventsText;
    [SerializeField] private TextMeshProUGUI conversationText;
    [SerializeField] private Button recordButton;
    [SerializeField] private Button connectButton;
    [SerializeField] private TextMeshProUGUI recordButtonText;
    [SerializeField] private TextMeshProUGUI connectButtonText;
    [SerializeField] private AudioController audioController;
    [SerializeField] private TMP_Dropdown listeningModeDropdown;

    [SerializeField] private Image[] frequencyBars;
    [SerializeField] private Image[] aiFrequencyBars;

    float maxFrequencyAmplitude = 0.1f;
    float aiMaxFrequencyAmplitude = 0.1f;

    bool isRecording = false;
    List<string> logMessages = new List<string>();

    float[] userBarAmplitudes;
    float[] aiBarAmplitudes;
    float barSmoothingSpeed = 5f;

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
        listeningModeDropdown.onValueChanged.AddListener(OnListeningModeChanged);
        UpdateRecordButton();
        UpdateListeningModeUI();

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

    private void UpdateRecordButton()
    {
        if (audioController.listeningMode == ListeningMode.PushToTalk)
        {
            recordButton.interactable = true;
            if (isRecording)
            {
                recordButton.image.color = Color.red;
                recordButtonText.text = "release to send";
                recordButtonText.color = Color.white;
            }
            else
            {
                recordButton.image.color = new Color(236f / 255f, 236f / 255f, 241f / 255f);
                recordButtonText.text = "push to talk";
                recordButtonText.color = new Color(50f / 255f, 50f / 255f, 50f / 255f);
            }
        }
        else
        {
            recordButton.interactable = false;
            recordButton.image.color = Color.clear;
            recordButtonText.text = "";
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
        connectButton.colors = cb;
    }

    private void OnSessionCreated()
    {
        AddLogMessage("session created.");
    }

    private void OnConversationItemCreated()
    {
        conversationText.text = "";
        AddLogMessage("conversation item created.");
    }

    private void OnResponseDone()
    {
        AddLogMessage("response done.");
    }

    private void OnTranscriptReceived(string transcriptPart)
    {
        conversationText.text += transcriptPart;
    }

    private void OnResponseCreated()
    {
        AddLogMessage("response created.");
    }

    private void OnListeningModeChanged(int index)
    {
        audioController.listeningMode = (ListeningMode)index;
        if (audioController.listeningMode == ListeningMode.VAD)
        {
            audioController.StartMicrophone();
            if (isRecording)
            {
                StopRecording();
            }
        }
        else
        {
            audioController.StopMicrophone();
        }
        UpdateRecordButton();
    }

    private void UpdateListeningModeUI()
    {
    }
}
