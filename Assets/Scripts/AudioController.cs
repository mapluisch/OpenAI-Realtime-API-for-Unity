using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioController : MonoBehaviour
{
    private AudioSource audioSource;
    public int sampleRate = 24000;
    public bool interruptResponseOnNewRecording = false;
    private bool isPlayingAudio = false;
    private bool cancelPending = false;
    private List<byte[]> audioBuffer = new List<byte[]>();

    public delegate void OnAudioRecorded(string base64Audio);
    public event OnAudioRecorded AudioRecorded;

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;
    }

    public void StartRecording()
    {
        if (interruptResponseOnNewRecording) CancelAudioPlayback();

        if (Microphone.devices.Length == 0) return;

        ResetCancelPending();

        string microphoneDevice = Microphone.devices[0];
        audioSource.clip = Microphone.Start(microphoneDevice, false, 10, sampleRate);
        Debug.Log("Recording started from microphone: " + microphoneDevice);
    }

    public void StopRecording()
    {
        if (Microphone.IsRecording(null))
        {
            Microphone.End(null);
            float[] audioData = new float[audioSource.clip.samples * audioSource.clip.channels];
            audioSource.clip.GetData(audioData, 0);

            string base64AudioData = ConvertFloatToPCM16AndBase64(audioData);
            AudioRecorded?.Invoke(base64AudioData);
        }
        else
        {
            Debug.LogWarning("No active recording found.");
        }
    }

    public void EnqueueAudioData(byte[] pcmAudioData)
    {
        if (cancelPending)
        {
            Debug.Log("Audio playback cancelled, not enqueuing new audio data.");
            return;
        }

        audioBuffer.Add(pcmAudioData);

        if (!isPlayingAudio)
        {
            PlayBufferedAudio();
        }
    }

    private void PlayBufferedAudio()
    {
        if (cancelPending)
        {
            ClearAudioBuffer();
            return;
        }

        if (audioBuffer.Count == 0)
        {
            Debug.Log("Audio buffer is empty, nothing to play.");
            isPlayingAudio = false;
            return;
        }

        isPlayingAudio = true;
        byte[] pcmAudioData = audioBuffer[0];
        audioBuffer.RemoveAt(0);

        float[] floatData = ConvertPCM16ToFloat(pcmAudioData);
        AudioClip clip = AudioClip.Create("BufferedAudio", floatData.Length, 1, sampleRate, false);
        clip.SetData(floatData, 0);
        audioSource.clip = clip;
        audioSource.Play();

        Debug.Log("Playing buffered audio...");
        Invoke("PlayBufferedAudio", clip.length);
    }

    public void CancelAudioPlayback()
    {
        Debug.Log("Cancelling audio playback.");
        cancelPending = true;
        ClearAudioBuffer();
    }

    private void ClearAudioBuffer()
    {
        Debug.Log("Clearing audio buffer.");
        audioBuffer.Clear();
        audioSource.Stop();
        isPlayingAudio = false;
    }

    public bool IsAudioPlaying()
    {
        return audioSource.isPlaying || audioBuffer.Count > 0;
    }


    private float[] ConvertPCM16ToFloat(byte[] pcmAudioData)
    {
        float[] floatData = new float[pcmAudioData.Length / 2];
        for (int i = 0; i < floatData.Length; i++)
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
            byte[] bytes = BitConverter.GetBytes(value);
            pcm16Audio[i * 2] = bytes[0];
            pcm16Audio[i * 2 + 1] = bytes[1];
        }
        return Convert.ToBase64String(pcm16Audio);
    }

    public void ResetCancelPending()
    {
        Debug.Log("Resetting cancel state.");
        cancelPending = false;
    }
}
