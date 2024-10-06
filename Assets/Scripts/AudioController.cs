using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioController : MonoBehaviour
{
    public ListeningMode listeningMode = ListeningMode.PushToTalk;
    public int sampleRate = 24000;
    [SerializeField] private bool interruptResponseOnNewRecording = false;
    [SerializeField] private float vadThreshold = 0.005f;
    [SerializeField] private float vadSilenceDuration = 2f;
    private bool isVADRecording = false;
    private float silenceTimer = 0f;
    private int lastSamplePosition = 0;
    private AudioClip microphoneClip;
    private AudioSource audioSource;
    private bool isPlayingAudio = false;
    private bool cancelPending = false;
    private string microphoneDevice;
    public float currentVolumeLevel = 0f;
    public float[] frequencyData { get; private set; }
    public int fftSampleSize = 1024;
    public float[] aiFrequencyData { get; private set; }
    public static event Action<string> OnAudioRecorded;
    public static event Action OnVADRecordingStarted;
    public static event Action OnVADRecordingEnded;
    private List<float> audioBuffer = new List<float>();
    private AudioClip playbackClip;
    private const int BUFFER_SIZE = 48000;
    private const float MIN_BUFFER_TIME = 0.1f;

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;
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
            UpdateCurrentVolumeAndFrequency();
            if (listeningMode == ListeningMode.VAD)
            {
                PerformVAD();
            }
        }
        else
        {
            frequencyData = null;
        }
        UpdateAIFrequencyData();
    }

    public void StartRecording()
    {
        if (interruptResponseOnNewRecording) CancelAudioPlayback();
        if (Microphone.devices.Length == 0) return;
        ResetCancelPending();
        microphoneDevice = Microphone.devices[0];
        microphoneClip = Microphone.Start(microphoneDevice, false, 10, sampleRate);
        lastSamplePosition = 0;
    }

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
                string base64AudioData = ConvertFloatToPCM16AndBase64(audioData);
                OnAudioRecorded?.Invoke(base64AudioData);
            }
        }
        frequencyData = null;
    }

    public void StartMicrophone()
    {
        if (Microphone.devices.Length == 0) return;
        microphoneDevice = Microphone.devices[0];
        microphoneClip = Microphone.Start(microphoneDevice, true, 10, sampleRate);
        lastSamplePosition = 0;
    }

    public void StopMicrophone()
    {
        if (Microphone.IsRecording(microphoneDevice)) Microphone.End(microphoneDevice);
        frequencyData = null;
    }

    private void UpdateCurrentVolumeAndFrequency()
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
        currentVolumeLevel = maxVolume;
        int fftSize = fftSampleSize;
        float[] fftSamples = new float[fftSize];
        int copyLength = Mathf.Min(samples.Length, fftSize);
        Array.Copy(samples, samples.Length - copyLength, fftSamples, 0, copyLength);
        frequencyData = new float[fftSize];
        FFT(fftSamples, frequencyData);
        lastSamplePosition = micPosition;
    }


    private void PerformVAD()
    {
        if (!Microphone.IsRecording(microphoneDevice)) return;

        if (currentVolumeLevel > vadThreshold && !isVADRecording)
        {
            silenceTimer = 0f;
            StartVADRecording();
        }
        else if (isVADRecording)
        {
            silenceTimer += Time.deltaTime;
            if (silenceTimer >= vadSilenceDuration) StopVADRecording();
        }
    }

    private void StartVADRecording()
    {
        if (interruptResponseOnNewRecording && !isVADRecording) CancelAudioPlayback();
        ResetCancelPending();
        isVADRecording = true;
        silenceTimer = 0f;
        microphoneClip = Microphone.Start(microphoneDevice, false, 10, sampleRate);
        OnVADRecordingStarted?.Invoke();
    }

    private void StopVADRecording()
    {
        if (Microphone.IsRecording(microphoneDevice))
        {
            int micPosition = Microphone.GetPosition(microphoneDevice);
            float[] audioData = new float[micPosition];
            microphoneClip.GetData(audioData, 0);
            string base64AudioData = ConvertFloatToPCM16AndBase64(audioData);
            OnAudioRecorded?.Invoke(base64AudioData);
        }

        isVADRecording = false;
        silenceTimer = 0f;
        OnVADRecordingEnded?.Invoke();

        Microphone.End(microphoneDevice);
        StartMicrophone();
    }


    public void EnqueueAudioData(byte[] pcmAudioData)
    {
        if (cancelPending) return;

        float[] floatData = ConvertPCM16ToFloat(pcmAudioData);
        audioBuffer.AddRange(floatData);

        if (!isPlayingAudio)
        {
            StartCoroutine(PlayAudioCoroutine());
        }
    }

    private IEnumerator PlayAudioCoroutine()
    {
        isPlayingAudio = true;

        while (isPlayingAudio)
        {
            if (audioBuffer.Count >= sampleRate * MIN_BUFFER_TIME)
            {
                int samplesToPlay = Mathf.Min(BUFFER_SIZE, audioBuffer.Count);
                float[] audioChunk = new float[samplesToPlay];
                audioBuffer.CopyTo(0, audioChunk, 0, samplesToPlay);
                audioBuffer.RemoveRange(0, samplesToPlay);

                playbackClip = AudioClip.Create("PlaybackClip", samplesToPlay, 1, sampleRate, false);
                playbackClip.SetData(audioChunk, 0);

                audioSource.clip = playbackClip;
                audioSource.Play();

                yield return new WaitForSeconds((float)samplesToPlay / sampleRate);
            }
            else if (audioBuffer.Count > 0)
            {
                float[] audioChunk = audioBuffer.ToArray();
                audioBuffer.Clear();

                playbackClip = AudioClip.Create("PlaybackClip", audioChunk.Length, 1, sampleRate, false);
                playbackClip.SetData(audioChunk, 0);

                audioSource.clip = playbackClip;
                audioSource.Play();

                yield return new WaitForSeconds((float)audioChunk.Length / sampleRate);
            }
            else if (audioBuffer.Count == 0 && !audioSource.isPlaying)
            {
                yield return new WaitForSeconds(0.1f);
                if (audioBuffer.Count == 0) isPlayingAudio = false;
            }
            else
            {
                yield return null;
            }
        }

        ClearAudioBuffer();
    }

    private void UpdateAIFrequencyData()
    {
        if (!audioSource.isPlaying)
        {
            aiFrequencyData = null;
            return;
        }
        int fftSize = fftSampleSize;
        aiFrequencyData = new float[fftSize];
        audioSource.GetSpectrumData(aiFrequencyData, 0, FFTWindow.BlackmanHarris);
    }

    public void CancelAudioPlayback()
    {
        cancelPending = true;
        StopAllCoroutines();
        ClearAudioBuffer();
    }

    private void ClearAudioBuffer()
    {
        audioBuffer.Clear();
        audioSource.Stop();
        isPlayingAudio = false;
        aiFrequencyData = null;
    }

    public bool IsAudioPlaying()
    {
        return audioSource.isPlaying || audioBuffer.Count > 0;
    }

    private float[] ConvertPCM16ToFloat(byte[] pcmAudioData)
    {
        int length = pcmAudioData.Length / 2;
        float[] floatData = new float[length];
        for (int i = 0; i < length; i++)
        {
            short sample = BitConverter.ToInt16(pcmAudioData, i * 2);
            floatData[i] = sample / 32768f;
        }
        return floatData;
    }

    private string ConvertFloatToPCM16AndBase64(float[] audioData)
    {
        byte[] pcm16Audio = new byte[audioData.Length * 2];
        for (int i = 0; i < audioData.Length; i++)
        {
            short value = (short)(Mathf.Clamp(audioData[i], -1f, 1f) * short.MaxValue);
            pcm16Audio[i * 2] = (byte)(value & 0xFF);
            pcm16Audio[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        return Convert.ToBase64String(pcm16Audio);
    }

    public void ResetCancelPending()
    {
        cancelPending = false;
    }

    private void FFT(float[] data, float[] spectrum)
    {
        int n = data.Length;
        int m = (int)Mathf.Log(n, 2);
        int j = 0;
        for (int i = 0; i < n; i++)
        {
            if (i < j)
            {
                float temp = data[i];
                data[i] = data[j];
                data[j] = temp;
            }
            int k = n >> 1;
            while (k >= 1 && k <= j)
            {
                j -= k;
                k >>= 1;
            }
            j += k;
        }
        for (int l = 1; l <= m; l++)
        {
            int le = 1 << l;
            int le2 = le >> 1;
            float ur = 1.0f;
            float ui = 0.0f;
            float sr = Mathf.Cos(Mathf.PI / le2);
            float si = -Mathf.Sin(Mathf.PI / le2);
            for (int j1 = 0; j1 < le2; j1++)
            {
                for (int i = j1; i < n; i += le)
                {
                    int ip = i + le2;
                    float tr = data[ip] * ur - 0 * ui;
                    float ti = data[ip] * ui + 0 * ur;
                    data[ip] = data[i] - tr;
                    data[i] += tr;
                }
                float temp = ur;
                ur = temp * sr - ui * si;
                ui = temp * si + ui * sr;
            }
        }
        for (int i = 0; i < n / 2; i++)
        {
            spectrum[i] = Mathf.Sqrt(data[i] * data[i] + data[n - i - 1] * data[n - i - 1]);
        }
    }
}
