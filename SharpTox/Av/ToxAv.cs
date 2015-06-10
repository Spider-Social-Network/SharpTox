﻿using SharpTox.Core;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SharpTox.Av
{
    /// <summary>
    /// Represents an instance of toxav.
    /// </summary>
    public class ToxAv : IDisposable
    {
        #region Callback delegates
        private ToxAvDelegates.CallCallback _onCallCallback;
        private ToxAvDelegates.CallStateCallback _onCallStateCallback;
        private ToxAvDelegates.AudioReceiveFrameCallback _onReceiveAudioFrameCallback;
        private ToxAvDelegates.VideoReceiveFrameCallback _onReceiveVideoFrameCallback;
        private ToxAvDelegates.BitrateStatusCallback _onAudioBitrateStatusCallback;
        private ToxAvDelegates.BitrateStatusCallback _onVideoBitrateStatusCallback;
        #endregion

        private List<ToxAvDelegates.GroupAudioReceiveCallback> _groupAudioHandlers = new List<ToxAvDelegates.GroupAudioReceiveCallback>();
        private bool _disposed = false;
        private bool _running = false;
        private CancellationTokenSource _cancelTokenSource;

        private ToxAvHandle _toxAv;

        /// <summary>
        /// The handle of this toxav instance.
        /// </summary>
        public ToxAvHandle Handle
        {
            get
            {
                return _toxAv;
            }
        }

        private ToxHandle _tox;

        /// <summary>
        /// The Tox instance that this toxav instance belongs to.
        /// </summary>
        public ToxHandle ToxHandle
        {
            get
            {
                return _tox;
            }
        }

        /// <summary>
        /// Initialises a new instance of toxav.
        /// </summary>
        /// <param name="tox"></param>
        public ToxAv(ToxHandle tox)
        {
            _tox = tox;

            var error = ToxAvErrorNew.Ok;
            _toxAv = ToxAvFunctions.New(tox, ref error);

            if (_toxAv == null || _toxAv.IsInvalid || error != ToxAvErrorNew.Ok)
                throw new Exception("Could not create a new instance of toxav.");

            //register audio/video callbacks early on
            //due to toxav being silly, we can't start calls without registering those beforehand
            RegisterAudioVideoCallbacks();
        }

        /// <summary>
        /// Initialises a new instance of toxav.
        /// </summary>
        /// <param name="tox"></param>
        public ToxAv(Tox tox)
            : this(tox.Handle) { }

        /// <summary>
        /// Releases all resources used by this instance of tox.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        //dispose pattern as described on msdn for a class that uses a safe handle
        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (_cancelTokenSource != null)
                {
                    _cancelTokenSource.Cancel();
                    _cancelTokenSource.Dispose();
                }
            }

            if (_toxAv != null && !_toxAv.IsInvalid && !_toxAv.IsClosed)
                _toxAv.Dispose();

            _disposed = true;
        }

        private void RegisterAudioVideoCallbacks()
        {
            _onReceiveAudioFrameCallback = (IntPtr toxAv, uint friendNumber, IntPtr pcm, uint sampleCount, byte channels, uint samplingRate, IntPtr userData) =>
            {
                if (OnAudioFrameReceived != null)
                    OnAudioFrameReceived(this, new ToxAvEventArgs.AudioFrameEventArgs((int)friendNumber, new ToxAvAudioFrame(pcm, sampleCount, samplingRate, channels)));
            };

            _onReceiveVideoFrameCallback = (IntPtr toxAv, uint friendNumber, ushort width, ushort height, IntPtr y, IntPtr u, IntPtr v, int yStride, int uStride, int vStride, IntPtr userData) =>
            {
                if (OnVideoFrameReceived != null)
                    OnVideoFrameReceived(this, new ToxAvEventArgs.VideoFrameEventArgs((int)friendNumber, new ToxAvVideoFrame(width, height, y, u, v, yStride, uStride, vStride)));
            };

            ToxAvFunctions.RegisterAudioReceiveFrameCallback(_toxAv, _onReceiveAudioFrameCallback, IntPtr.Zero);
            ToxAvFunctions.RegisterVideoReceiveFrameCallback(_toxAv, _onReceiveVideoFrameCallback, IntPtr.Zero);
        }

        /// <summary>
        /// Starts the main toxav_do loop.
        /// </summary>
        public void Start()
        {
            ThrowIfDisposed();

            if (_running)
                return;

            Loop();
        }

        /// <summary>
        /// Stops the main toxav_do loop if it's running.
        /// </summary>
        public void Stop()
        {
            ThrowIfDisposed();

            if (!_running)
                return;

            if (_cancelTokenSource != null)
            {
                _cancelTokenSource.Cancel();
                _cancelTokenSource.Dispose();

                _running = false;
            }
        }

        private void Loop()
        {
            _cancelTokenSource = new CancellationTokenSource();
            _running = true;

            Task.Factory.StartNew(async () =>
            {
                while (_running)
                {
                    if (_cancelTokenSource.IsCancellationRequested)
                        break;

                    int delay = DoIterate();
                    await Task.Delay(delay);
                }
            }, _cancelTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>
        /// Runs the loop once in the current thread and returns the next timeout.
        /// </summary>
        public int Iterate()
        {
            ThrowIfDisposed();

            if (_running)
                throw new Exception("Loop already running");

            return DoIterate();
        }

        private int DoIterate()
        {
            ToxAvFunctions.Iterate(_toxAv);
            return (int)ToxAvFunctions.IterationInterval(_toxAv);
        }

        public bool Call(int friendNumber, int audioBitrate, int videoBitrate, out ToxAvErrorCall error)
        {
            ThrowIfDisposed();

            error = ToxAvErrorCall.Ok;
            return ToxAvFunctions.Call(_toxAv, (uint)friendNumber, (uint)audioBitrate, (uint)videoBitrate, ref error);
        }

        public bool Call(int friendNumber, int audioBitrate, int videoBitrate)
        {
            var error = ToxAvErrorCall.Ok;
            return Call(friendNumber, audioBitrate, videoBitrate, out error);
        }

        public bool Answer(int friendNumber, int audioBitrate, int videoBitrate, out ToxAvErrorAnswer error)
        {
            ThrowIfDisposed();

            error = ToxAvErrorAnswer.Ok;
            return ToxAvFunctions.Answer(_toxAv, (uint)friendNumber, (uint)audioBitrate, (uint)videoBitrate, ref error);
        }

        public bool Answer(int friendNumber, int audioBitrate, int videoBitrate)
        {
            var error = ToxAvErrorAnswer.Ok;
            return Answer(friendNumber, audioBitrate, videoBitrate, out error);
        }

        public bool SendControl(int friendNumber, ToxAvCallControl control, out ToxAvErrorCallControl error)
        {
            ThrowIfDisposed();

            error = ToxAvErrorCallControl.Ok;
            return ToxAvFunctions.CallControl(_toxAv, (uint)friendNumber, control, ref error);
        }

        public bool SendControl(int friendNumber, ToxAvCallControl control)
        {
            var error = ToxAvErrorCallControl.Ok;
            return SendControl(friendNumber, control, out error);
        }

        public bool SetAudioBitrate(int friendNumber, int bitrate, bool force, out ToxAvErrorBitrate error)
        {
            ThrowIfDisposed();

            error = ToxAvErrorBitrate.Ok;
            return ToxAvFunctions.AudioBitrateSet(_toxAv, (uint)friendNumber, (uint)bitrate, force, ref error);
        }

        public bool SetAudioBitrate(int friendNumber, int bitrate, bool force)
        {
            var error = ToxAvErrorBitrate.Ok;
            return SetAudioBitrate(friendNumber, bitrate, force, out error);
        }

        public bool SetVideoBitrate(int friendNumber, int bitrate, bool force, out ToxAvErrorBitrate error)
        {
            ThrowIfDisposed();

            error = ToxAvErrorBitrate.Ok;
            return ToxAvFunctions.VideoBitrateSet(_toxAv, (uint)friendNumber, (uint)bitrate, force, ref error);
        }

        public bool SetVideoBitrate(int friendNumber, int bitrate, bool force)
        {
            var error = ToxAvErrorBitrate.Ok;
            return SetVideoBitrate(friendNumber, bitrate, force, out error);
        }

        public bool SendVideoFrame(int friendNumber, ToxAvVideoFrame frame, out ToxAvErrorSendFrame error)
        {
            ThrowIfDisposed();

            error = ToxAvErrorSendFrame.Ok;
            return ToxAvFunctions.VideoSendFrame(_toxAv, (uint)friendNumber, (ushort)frame.Width, (ushort)frame.Height, frame.Y, frame.U, frame.V, ref error);
        }

        public bool SendVideoFrame(int friendNumber, ToxAvVideoFrame frame)
        {
            var error = ToxAvErrorSendFrame.Ok;
            return SendVideoFrame(friendNumber, frame, out error);
        }

        public bool SendAudioFrame(int friendNumber, ToxAvAudioFrame frame, out ToxAvErrorSendFrame error)
        {
            ThrowIfDisposed();

            error = ToxAvErrorSendFrame.Ok;
            return ToxAvFunctions.AudioSendFrame(_toxAv, (uint)friendNumber, frame.Data, (uint)(frame.Data.Length / frame.Channels), (byte)frame.Channels, (uint)frame.SamplingRate, ref error);
        }

        public bool SendAudioFrame(int friendNumber, ToxAvAudioFrame frame)
        {
            var error = ToxAvErrorSendFrame.Ok;
            return SendAudioFrame(friendNumber, frame, out error);
        }

        /// <summary>
        /// Creates a new audio groupchat.
        /// </summary>
        /// <returns></returns>
        public int AddAvGroupchat()
        {
            ThrowIfDisposed();

            ToxAvDelegates.GroupAudioReceiveCallback callback = (IntPtr tox, int groupNumber, int peerNumber, IntPtr frame, uint sampleCount, byte channels, uint sampleRate, IntPtr userData) =>
            {
                if (OnReceivedGroupAudio != null)
                {
                    short[] samples = new short[sampleCount * channels];
                    Marshal.Copy(frame, samples, 0, samples.Length);

                    OnReceivedGroupAudio(this, new ToxAvEventArgs.GroupAudioDataEventArgs(groupNumber, peerNumber, samples, (int)channels, (int)sampleRate));
                }
            };

            int result = ToxAvFunctions.AddAvGroupchat(_tox, callback, IntPtr.Zero);
            if (result != -1)
                _groupAudioHandlers.Add(callback);

            return result;
        }

        /// <summary>
        /// Joins an audio groupchat.
        /// </summary>
        /// <param name="friendNumber"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public int JoinAvGroupchat(int friendNumber, byte[] data)
        {
            ThrowIfDisposed();

            if (data == null)
                throw new ArgumentNullException("data");

            ToxAvDelegates.GroupAudioReceiveCallback callback = (IntPtr tox, int groupNumber, int peerNumber, IntPtr frame, uint sampleCount, byte channels, uint sampleRate, IntPtr userData) =>
            {
                if (OnReceivedGroupAudio != null)
                {
                    short[] samples = new short[sampleCount * channels];
                    Marshal.Copy(frame, samples, 0, samples.Length);

                    OnReceivedGroupAudio(this, new ToxAvEventArgs.GroupAudioDataEventArgs(groupNumber, peerNumber, samples, (int)channels, (int)sampleRate));
                }
            };

            int result = ToxAvFunctions.JoinAvGroupchat(_tox, friendNumber, data, (ushort)data.Length, callback, IntPtr.Zero);
            if (result != -1)
                _groupAudioHandlers.Add(callback);

            return result;
        }

        /// <summary>
        /// Sends an audio frame to a group.
        /// </summary>
        /// <param name="groupNumber"></param>
        /// <param name="pcm"></param>
        /// <param name="perframe"></param>
        /// <param name="channels"></param>
        /// <param name="sampleRate"></param>
        /// <returns></returns>
        public bool GroupSendAudio(int groupNumber, short[] pcm, int perframe, int channels, int sampleRate)
        {
            ThrowIfDisposed();

            return ToxAvFunctions.GroupSendAudio(_tox, groupNumber, pcm, (uint)perframe, (byte)channels, (uint)sampleRate) == 0;
        }

        #region Events
        private EventHandler<ToxAvEventArgs.CallRequestEventArgs> _onCall;

        /// <summary>
        /// Occurs when a friend sends a call request.
        /// </summary>
        public event EventHandler<ToxAvEventArgs.CallRequestEventArgs> OnCallRequestReceived
        {
            add
            {
                if (_onCallCallback == null)
                {
                    _onCallCallback = (IntPtr toxAv, uint friendNumber, bool audioEnabled, bool videoEnabled, IntPtr userData) =>
                    {
                        if (_onCall != null)
                            _onCall(this, new ToxAvEventArgs.CallRequestEventArgs((int)friendNumber, audioEnabled, videoEnabled));
                    };

                    ToxAvFunctions.RegisterCallCallback(_toxAv, _onCallCallback, IntPtr.Zero);
                }

                _onCall += value;
            }
            remove
            {
                if (_onCall.GetInvocationList().Length == 1)
                {
                    ToxAvFunctions.RegisterCallCallback(_toxAv, null, IntPtr.Zero);
                    _onCallCallback = null;
                }

                _onCall -= value;
            }
        }

        private EventHandler<ToxAvEventArgs.CallStateEventArgs> _onCallState;

        /// <summary>
        /// Occurs when the state of a call changed.
        /// </summary>
        public event EventHandler<ToxAvEventArgs.CallStateEventArgs> OnCallStateChanged
        {
            add
            {
                if (_onCallStateCallback == null)
                {
                    _onCallStateCallback = (IntPtr toxAv, uint friendNumber, ToxAvCallState state, IntPtr userData) =>
                    {
                        if (_onCallState != null)
                            _onCallState(this, new ToxAvEventArgs.CallStateEventArgs((int)friendNumber, state));
                    };

                    ToxAvFunctions.RegisterCallStateCallback(_toxAv, _onCallStateCallback, IntPtr.Zero);
                }

                _onCallState += value;
            }
            remove
            {
                if (_onCallState.GetInvocationList().Length == 1)
                {
                    ToxAvFunctions.RegisterCallStateCallback(_toxAv, null, IntPtr.Zero);
                    _onCallStateCallback = null;
                }

                _onCallState -= value;
            }
        }

        private EventHandler<ToxAvEventArgs.BitrateStatusEventArgs> _onAudioBitrateStatus;

        /// <summary>
        /// Occurs when a friend changed their audio bitrate during a call.
        /// </summary>
        public event EventHandler<ToxAvEventArgs.BitrateStatusEventArgs> OnAudioBitrateChanged
        {
            add
            {
                if (_onAudioBitrateStatusCallback == null)
                {
                    _onAudioBitrateStatusCallback = (IntPtr toxAv, uint friendNumber, bool stable, uint bitrate, IntPtr userData) =>
                    {
                        if (_onAudioBitrateStatus != null)
                            _onAudioBitrateStatus(this, new ToxAvEventArgs.BitrateStatusEventArgs((int)friendNumber, stable, (int)bitrate));
                    };

                    ToxAvFunctions.RegisterAudioBitrateStatusCallback(_toxAv, _onAudioBitrateStatusCallback, IntPtr.Zero);
                }

                _onAudioBitrateStatus += value;
            }
            remove
            {
                if (_onAudioBitrateStatus.GetInvocationList().Length == 1)
                {
                    ToxAvFunctions.RegisterAudioBitrateStatusCallback(_toxAv, null, IntPtr.Zero);
                    _onAudioBitrateStatusCallback = null;
                }

                _onAudioBitrateStatus -= value;
            }
        }

        private EventHandler<ToxAvEventArgs.BitrateStatusEventArgs> _onVideoBitrateStatus;

        /// <summary>
        /// Occurs when a friend changed their video bitrate during a call.
        /// </summary>
        public event EventHandler<ToxAvEventArgs.BitrateStatusEventArgs> OnVideoBitrateChanged
        {
            add
            {
                if (_onVideoBitrateStatusCallback == null)
                {
                    _onVideoBitrateStatusCallback = (IntPtr toxAv, uint friendNumber, bool stable, uint bitrate, IntPtr userData) =>
                    {
                        if (_onVideoBitrateStatus != null)
                            _onVideoBitrateStatus(this, new ToxAvEventArgs.BitrateStatusEventArgs((int)friendNumber, stable, (int)bitrate));
                    };

                    ToxAvFunctions.RegisterVideoBitrateStatusCallback(_toxAv, _onVideoBitrateStatusCallback, IntPtr.Zero);
                }

                _onVideoBitrateStatus += value;
            }
            remove
            {
                if (_onVideoBitrateStatus.GetInvocationList().Length == 1)
                {
                    ToxAvFunctions.RegisterVideoBitrateStatusCallback(_toxAv, null, IntPtr.Zero);
                    _onVideoBitrateStatusCallback = null;
                }

                _onVideoBitrateStatus -= value;
            }
        }

        /// <summary>
        /// Occurs when an video frame is received.
        /// </summary>
        public event EventHandler<ToxAvEventArgs.VideoFrameEventArgs> OnVideoFrameReceived;

        /// <summary>
        /// Occurs when an audio frame is received.
        /// </summary>
        public event EventHandler<ToxAvEventArgs.AudioFrameEventArgs> OnAudioFrameReceived;

        /// <summary>
        /// Occurs when an audio frame was received from a group.
        /// </summary>
        public event EventHandler<ToxAvEventArgs.GroupAudioDataEventArgs> OnReceivedGroupAudio;

        #endregion

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().FullName);
        }
    }
}
