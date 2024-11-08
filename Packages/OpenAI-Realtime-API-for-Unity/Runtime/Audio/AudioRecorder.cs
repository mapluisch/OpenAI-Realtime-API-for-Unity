using System;
using System.Collections.Generic;
using UnityEngine;

public class AudioRecorder : MonoBehaviour
{
    public ListeningMode listeningMode = ListeningMode.PushToTalk;
    public int sampleRate = 24000;
    [SerializeField] private bool interruptResponseOnNewRecording = false;
    [SerializeField] private float vadEnergyThreshold = 0.5f;
    [SerializeField] private float vadSilenceDuration = 2f;
    private float vadLastSec = 1.0f;
    [SerializeField] private float vadFreqThreshold = 0.0f;
    private bool isVADRecording = false;
    private float silenceTimer = 0f;
    private int lastSamplePosition = 0;
    private AudioClip microphoneClip;
    private string microphoneDevice;
    public float[] frequencyData { get; private set; }
    public int fftSampleSize = 1024;
    private List<float> audioDataBuffer = new List<float>();
    private int vadRecordingStartIndex = 0;
    private const int MAX_BUFFER_LENGTH_SEC = 10;
    public static event Action<string> OnAudioRecorded;
    public static event Action OnVADRecordingStarted;
    public static event Action OnVADRecordingEnded;
    private AudioPlayer audioPlayer;

    private void Start()
    {
        audioPlayer = GetComponent<AudioPlayer>();
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone devices found.");
            return;
        }
        microphoneDevice = Microphone.devices[0];
        if (listeningMode == ListeningMode.VAD)
        {
            StartMicrophone();
        }
    }

    private void Update()
    {
        if (Microphone.IsRecording(microphoneDevice))
        {
            UpdateCurrentFrequency();
            if (listeningMode == ListeningMode.VAD)
            {
                PerformVAD();
            }
        }
        else
        {
            frequencyData = null;
        }
    }

    /// <summary>
    /// starts recording audio
    /// </summary>
    public void StartRecording()
    {
        if (interruptResponseOnNewRecording) audioPlayer.CancelAudioPlayback();
        audioPlayer.ResetCancelPending();
        microphoneDevice = Microphone.devices[0];
        microphoneClip = Microphone.Start(microphoneDevice, false, 10, sampleRate);
        lastSamplePosition = 0;
    }

    /// <summary>
    /// stops recording audio
    /// </summary>
    public void StopRecording()
    {
        if (Microphone.IsRecording(microphoneDevice))
        {
            int micPosition = Microphone.GetPosition(microphoneDevice);
            int samples = micPosition;
            float[] audioData = new float[samples];
            if (microphoneClip != null && micPosition != 0)
            {
                microphoneClip.GetData(audioData, 0);
                Microphone.End(microphoneDevice);
                string base64AudioData = AudioProcessingUtils.ConvertFloatToPCM16AndBase64(audioData);
                OnAudioRecorded?.Invoke(base64AudioData);
            }
        }
        frequencyData = null;
    }

    /// <summary>
    /// starts the microphone in loop mode
    /// </summary>
    public void StartMicrophone()
    {
        microphoneDevice = Microphone.devices[0];
        microphoneClip = Microphone.Start(microphoneDevice, true, 10, sampleRate);
        lastSamplePosition = 0;
    }

    /// <summary>
    /// stops the microphone
    /// </summary>
    public void StopMicrophone()
    {
        if (Microphone.IsRecording(microphoneDevice)) Microphone.End(microphoneDevice);
        frequencyData = null;
    }

    /// <summary>
    /// updates fft frequency bands for vad/visualization
    /// </summary>
    private void UpdateCurrentFrequency()
    {
        int micPosition = Microphone.GetPosition(microphoneDevice);
        int sampleDiff = micPosition - lastSamplePosition;
        if (sampleDiff < 0)
        {
            sampleDiff += microphoneClip.samples;
        }
        if (sampleDiff == 0)
        {
            return;
        }
        float[] samples = new float[sampleDiff];
        int startPosition = lastSamplePosition;
        if (startPosition + sampleDiff <= microphoneClip.samples)
        {
            microphoneClip.GetData(samples, startPosition);
        }
        else
        {
            int samplesToEnd = microphoneClip.samples - startPosition;
            int samplesFromStart = sampleDiff - samplesToEnd;
            float[] samplesPart1 = new float[samplesToEnd];
            float[] samplesPart2 = new float[samplesFromStart];
            microphoneClip.GetData(samplesPart1, startPosition);
            microphoneClip.GetData(samplesPart2, 0);
            Array.Copy(samplesPart1, 0, samples, 0, samplesToEnd);
            Array.Copy(samplesPart2, 0, samples, samplesToEnd, samplesFromStart);
        }
        float maxVolume = 0f;
        foreach (var sample in samples)
        {
            float absSample = Mathf.Abs(sample);
            if (absSample > maxVolume)
            {
                maxVolume = absSample;
            }
        }
        int fftSize = fftSampleSize;
        float[] fftSamples = new float[fftSize];
        int copyLength = Mathf.Min(samples.Length, fftSize);
        Array.Copy(samples, samples.Length - copyLength, fftSamples, 0, copyLength);
        frequencyData = new float[fftSize];
        AudioProcessingUtils.FFT(fftSamples, frequencyData);
        lastSamplePosition = micPosition;
        audioDataBuffer.AddRange(samples);
        int maxBufferLengthSamples = sampleRate * MAX_BUFFER_LENGTH_SEC;
        if (audioDataBuffer.Count > maxBufferLengthSamples)
        {
            int excessSamples = audioDataBuffer.Count - maxBufferLengthSamples;
            audioDataBuffer.RemoveRange(0, excessSamples);
            vadRecordingStartIndex -= excessSamples;
            if (vadRecordingStartIndex < 0) vadRecordingStartIndex = 0;
        }
    }

    /// <summary>
    /// checks if speech is detected (AudioProcessingUtils.SimpleVad) and starts vad rec
    /// </summary>
    private void PerformVAD()
    {
        if (!Microphone.IsRecording(microphoneDevice)) return;
        bool hasSpeech = AudioProcessingUtils.SimpleVad(audioDataBuffer.ToArray(), sampleRate, vadLastSec, vadEnergyThreshold, vadFreqThreshold);
        if (hasSpeech)
        {
            silenceTimer = 0f;
            if (!isVADRecording) StartVADRecording();
        }
        else if (isVADRecording)
        {
            silenceTimer += Time.deltaTime;
            if (silenceTimer >= vadSilenceDuration) StopVADRecording();
        }
    }

    /// <summary>
    /// starts vad recording, i.e. by interrupting / canceling current responses and setting the recording index
    /// </summary>
    private void StartVADRecording()
    {
        if (interruptResponseOnNewRecording && !isVADRecording) audioPlayer.CancelAudioPlayback();
        audioPlayer.ResetCancelPending();
        isVADRecording = true;
        silenceTimer = 0f;
        vadRecordingStartIndex = audioDataBuffer.Count;
        OnVADRecordingStarted?.Invoke();
    }

    /// <summary>
    /// stops vad recording and processes recorded vad snippet
    /// </summary>
    private void StopVADRecording()
    {
        if (isVADRecording)
        {
            int recordingLength = audioDataBuffer.Count - vadRecordingStartIndex;
            if (recordingLength > 0)
            {
                float[] audioData = audioDataBuffer.GetRange(vadRecordingStartIndex, recordingLength).ToArray();
                string base64AudioData = AudioProcessingUtils.ConvertFloatToPCM16AndBase64(audioData);
                OnAudioRecorded?.Invoke(base64AudioData);
            }
        }
        isVADRecording = false;
        silenceTimer = 0f;
        OnVADRecordingEnded?.Invoke();
    }
}
