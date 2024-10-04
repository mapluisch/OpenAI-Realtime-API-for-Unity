using System;
using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class AudioController : MonoBehaviour
{
    private AudioSource audioSource;
    public int sampleRate = 24000;
    private bool isPlayingAudio = false;
    private List<byte> audioBuffer = new List<byte>();

    public delegate void OnAudioRecorded(string base64Audio);
    public event OnAudioRecorded AudioRecorded;

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;
    }

    public void StartRecording()
    {
        if (Microphone.devices.Length == 0) return;

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
        audioBuffer.AddRange(pcmAudioData);

        if (!isPlayingAudio)
        {
            PlayBufferedAudio();
        }
    }

    private void PlayBufferedAudio()
    {
        if (audioBuffer.Count == 0)
        {
            Debug.Log("Audio buffer is empty.");
            isPlayingAudio = false;
            return;
        }

        isPlayingAudio = true;
        float[] floatData = ConvertPCM16ToFloat(audioBuffer.ToArray());
        AudioClip clip = AudioClip.Create("BufferedAudio", floatData.Length, 1, sampleRate, false);
        clip.SetData(floatData, 0);
        audioSource.clip = clip;
        audioSource.Play();

        Debug.Log("Playing buffered audio...");
        audioBuffer.Clear();
        Invoke("PlayBufferedAudio", clip.length);
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
        return System.Convert.ToBase64String(pcm16Audio);
    }
}
