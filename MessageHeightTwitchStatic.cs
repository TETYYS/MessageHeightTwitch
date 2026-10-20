using MessageHeightTwitch.Emotes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using MessageHeightTwitch;

/// <summary>
/// Native entry points and channel registrations for chat measurement.
/// </summary>
internal static class MessageHeightTwitchStatic
{
    internal enum EmoteErrorCode
    {
        FFZFetchFailed = 1,
        BTTVFetchFailed = 2,
        SevenTVFetchFailed = 3,
        TwitchFetchFailed = 4,
        ChannelInitializationFailed = 5,
        GlobalInitializationFailed = 6,
        MeasurementFailed = 7
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TwitchEmote
    {
        public byte* Name;
        public byte* Url;
    }

    /// <summary>
    /// Publishes and stops one channel measurer and routes loading errors.
    /// </summary>
    internal sealed class ChannelRegistration
    {
        private readonly Lock Lock = new();
        private bool Stopped;
        internal readonly CancellationTokenSource Lifetime = new();
        private readonly ConcurrentDictionary<string, byte> PendingImages = new();
        private readonly int TimeoutMs;
        internal MessageHeightTwitch.MessageHeightTwitch? Instance;
        internal readonly Action<EmoteErrorCode, string>? OnError;

        internal ChannelRegistration(int TimeoutMs, Action<EmoteErrorCode, string>? OnError)
        {
            this.TimeoutMs = TimeoutMs;
            this.OnError = OnError;
        }

        internal void LoadImages(MessageHeightTwitch.MessageHeightTwitch Instance, Dictionary<string, string>.ValueCollection Urls)
        {
            if (Instance.TwitchImages is not { } images)
                return;
            foreach (var url in Urls)
                if (!images.IsLoaded(url) && PendingImages.TryAdd(url, 0))
                    _ = LoadImage(Instance, url);
        }

        private async Task LoadImage(MessageHeightTwitch.MessageHeightTwitch Instance, string Url)
        {
            try {
                await Retry(token => Instance.LoadTwitchEmotes([Url], token), TimeoutMs, ex => Report(EmoteErrorCode.TwitchFetchFailed, $"{Url}: {ex.Message}"), Lifetime.Token).ConfigureAwait(false);
            } finally {
                PendingImages.TryRemove(Url, out _);
            }
        }

        internal void Publish(MessageHeightTwitch.MessageHeightTwitch Instance)
        {
            lock (Lock) {
                if (!Stopped) {
                    Volatile.Write(ref this.Instance, Instance);
                    return;
                }
            }
            Instance.Dispose();
        }

        internal void Stop()
        {
            lock (Lock)
                Stopped = true;
            Lifetime.Cancel();
            Volatile.Read(ref Instance)?.Dispose();
        }

        internal void Report(EmoteErrorCode Code, string Message)
        {
            lock (Lock)
                if (Stopped)
                    return;
            try { OnError?.Invoke(Code, Message); } catch { }
        }
    }

    private static readonly ConcurrentDictionary<string, ChannelRegistration> Instances = new();
    private static readonly Lock RegistrationLock = new();

    private static void Register(string Name, ChannelRegistration Registration)
    {
        ChannelRegistration? previous;
        lock (RegistrationLock) {
            Instances.TryGetValue(Name, out previous);
            Instances[Name] = Registration;
        }
        previous?.Stop();
    }

    [UnmanagedCallersOnly(EntryPoint = "InitMessageHeightTwitch", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe int InitMessageHeightTwitch(byte* FontDir, int TimeoutMs, byte Enable7TvEmotes,
        delegate* unmanaged[Cdecl]<void*, byte*, int, byte*, void> OnError, void* UserData)
    {
        if (TimeoutMs < -1) return 0;
        var callback = (nint)OnError;
        var context = (nint)UserData;
        try {
            var directory = Marshal.PtrToStringUTF8((nint)FontDir);
            MessageHeightTwitch.MessageHeightTwitch.InitFonts(directory);
            _ = Retry(token => MessageHeightTwitch.MessageHeightTwitch.Init(directory, Enable7TvEmotes != 0, token), TimeoutMs,
                ex => { if (callback != 0) InvokeError(callback, context, "", EmoteErrorCode.GlobalInitializationFailed, ex.Message); },
                CancellationToken.None);
            return 1;
        } catch (Exception ex) {
            try {
                if (callback != 0) InvokeError(callback, context, "", EmoteErrorCode.GlobalInitializationFailed, ex.Message);
            } catch { }
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "InitChannel", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe int InitChannel(byte* Channel, byte* ChannelId, int TimeoutMs, byte Enable7TvEmotes, delegate* unmanaged[Cdecl]<void*, byte*, int, byte*, void> OnError, void* UserData)
    {
        try {
            var name = Marshal.PtrToStringUTF8((nint)Channel);
            var id = Marshal.PtrToStringUTF8((nint)ChannelId);

            if (name == null || id == null || TimeoutMs < -1 || !GlobalEmotes.Shared.IsInitialized(Enable7TvEmotes != 0))
                return 0;

            var callbackAddress = (nint)OnError;
            var context = (nint)UserData;

            return QueueChannel(name, id, TimeoutMs, Enable7TvEmotes != 0, callbackAddress == 0 ? null : (code, message) => InvokeError(callbackAddress, context, name, code, message));
        } catch {
            return 0;
        }
    }

    private static unsafe void InvokeError(nint Callback, nint Context, string Channel, EmoteErrorCode Code, string Message)
    {
        var name = Marshal.StringToCoTaskMemUTF8(Channel);
        var detail = Marshal.StringToCoTaskMemUTF8(Message);

        try {
            ((delegate* unmanaged[Cdecl]<void*, byte*, int, byte*, void>)Callback)((void*)Context, (byte*)name, (int)Code, (byte*)detail);
        } finally {
            Marshal.FreeCoTaskMem(detail);
            Marshal.FreeCoTaskMem(name);
        }
    }

    internal static int QueueChannel(string Name, string Id, int TimeoutMs, bool Enable7TvEmotes, Action<EmoteErrorCode, string>? OnError, Func<MessageHeightTwitch.MessageHeightTwitch>? Create = null, HttpClient? Client = null, GlobalEmotes? Globals = null)
    {
        if (TimeoutMs < -1)
            return 0;

        var registration = new ChannelRegistration(TimeoutMs, OnError);
        try {
            var instance = Create != null ? Create() : new MessageHeightTwitch.MessageHeightTwitch(Enable7TvEmotes, Client, Globals);
            registration.Publish(instance);
            Register(Name, registration);
            if (Create == null)
                _ = Task.Run(() => Retry(token => instance.LoadEmotes(Name, Id, Enable7TvEmotes, token), TimeoutMs, ex => registration.Report(EmoteErrorCode.ChannelInitializationFailed, ex.Message), registration.Lifetime.Token));
            return 1;
        } catch (Exception ex) {
            registration.Report(EmoteErrorCode.ChannelInitializationFailed, ex.ToString());
            registration.Stop();
            return 0;
        }
    }

    // All native-only policies live at this boundary. Managed operations receive only a token and throw.
    internal static async Task Retry(Func<CancellationToken, Task> Operation, int TimeoutMs, Action<Exception> OnError,
        CancellationToken Token, Func<TimeSpan, CancellationToken, Task>? Delay = null)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!Token.IsCancellationRequested) {
            try {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(Token);
                attempt.CancelAfter(TimeoutMs);
                await Operation(attempt.Token).WaitAsync(attempt.Token).ConfigureAwait(false);
                return;
            } catch (OperationCanceledException) when (Token.IsCancellationRequested) {
                return;
            } catch (Exception ex) {
                try { OnError(ex); } catch { }
                if (ex is not (HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException))
                    return;
            }
            try {
                await (Delay ?? Task.Delay)(backoff, Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (Token.IsCancellationRequested) {
                return;
            }
            backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "CalculateMessageHeight", CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe float CalculateMessageHeight(byte* Channel, byte* Input, byte* Username, byte* DisplayName, int NumberOfBadges, TwitchEmote* TwitchEmotes, int TwitchEmotesLen)
    {
        ChannelRegistration? registration = null;
        try {
            var name = Marshal.PtrToStringUTF8((nint)Channel);
            var message = Marshal.PtrToStringUTF8((nint)Input);
            var user = Marshal.PtrToStringUTF8((nint)Username);
            var display = Marshal.PtrToStringUTF8((nint)DisplayName);

            if (name == null || message == null || user == null || display == null || TwitchEmotesLen < 0 || (TwitchEmotesLen > 0 && TwitchEmotes == null) || !Instances.TryGetValue(name, out registration) || Volatile.Read(ref registration.Instance) is not { } instance)
                return 0;

            var emotes = new Dictionary<string, string>(TwitchEmotesLen);
            for (var x = 0;x < TwitchEmotesLen;x++) {
                var emoteName = Marshal.PtrToStringUTF8((nint)TwitchEmotes[x].Name);
                var url = Marshal.PtrToStringUTF8((nint)TwitchEmotes[x].Url);

                if (emoteName == null || url == null)
                    return 0;
                emotes[emoteName] = url;
            }

            registration.LoadImages(instance, emotes.Values);
            return instance.CalculateMessageHeight(message, user, display, NumberOfBadges, emotes);
        } catch (Exception ex) {
            registration?.Report(EmoteErrorCode.MeasurementFailed, ex.ToString());
            return 0;
        }
    }
}
