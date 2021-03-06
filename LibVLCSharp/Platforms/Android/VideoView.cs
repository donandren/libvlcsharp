﻿using System;

using Android.Content;
using Android.Runtime;
using Android.Util;
using Android.Views;

using LibVLCSharp.Shared;

using Org.Videolan.Libvlc;

namespace LibVLCSharp.Platforms.Android
{
    public class VideoView : SurfaceView, IVLCVoutCallback, IVideoView
    {
        MediaPlayer _mediaPlayer;
        LibVLC _libVLC;
        AWindow _awindow;
        LayoutChangeListener _layoutListener;

        #region ctors

        public VideoView(IntPtr javaReference, JniHandleOwnership transfer, string[] cliOptions = default (string[])) : base(javaReference, transfer) => Init(cliOptions);

        public VideoView(Context context, string[] cliOptions = default(string[])) : base(context) => Init(cliOptions);

        public VideoView(Context context, IAttributeSet attrs, string[] cliOptions = default(string[])) : base(context, attrs) => Init(cliOptions);

        public VideoView(Context context, IAttributeSet attrs, int defStyleAttr, string[] cliOptions = default(string[])) : base(context, attrs, defStyleAttr) => Init(cliOptions);

        public VideoView(Context context, IAttributeSet attrs, int defStyleAttr, int defStyleRes, string[] cliOptions = default(string[])) 
            : base(context, attrs, defStyleAttr, defStyleRes) => Init(cliOptions);

        #endregion

        public LibVLCSharp.Shared.MediaPlayer MediaPlayer => _mediaPlayer;
        public LibVLC LibVLC => _libVLC;

        void Attach()
        {
            _awindow = new AWindow(new SurfaceCallback(_mediaPlayer));
            _awindow.AddCallback(this);
            _awindow.SetVideoView(this);
            _awindow.AttachViews();

            _mediaPlayer.SetAndroidContext(_awindow.Handle);

            _layoutListener = new LayoutChangeListener(_awindow);
            AddOnLayoutChangeListener(_layoutListener);
        }

        void Detach()
        {
            _awindow.RemoveCallback(this);
            _awindow.DetachViews();

            _mediaPlayer.SetAndroidContext(IntPtr.Zero);

            RemoveOnLayoutChangeListener(_layoutListener);
            _layoutListener.Dispose();
            _layoutListener = null;

            _awindow.Dispose();
            _awindow = null;
        }

        public virtual void OnSurfacesCreated(IVLCVout vout)
        {
        }

        public virtual void OnSurfacesDestroyed(IVLCVout vout)
        {
        }

        void Init(string[] cliOptions)
        {
            Core.Initialize();

            _libVLC = new LibVLC(cliOptions);
            _mediaPlayer = new MediaPlayer(_libVLC);

            Attach();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            Detach();

            _mediaPlayer.Media?.Dispose();
            _mediaPlayer.Dispose();
            _libVLC.Dispose();
        }
    }
}