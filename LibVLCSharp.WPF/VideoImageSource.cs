#define NET45

namespace LibVLCSharp.WPF
{
    using System;

#if NET45

    using System.IO.MemoryMappedFiles;

#endif

    using System.Runtime.InteropServices;
    using System.Windows.Interop;
    using System.ComponentModel;
    using System.Windows.Threading;
    using System.Text;
    using vlc = LibVLCSharp.Shared;
    using System.Windows.Media;

    /// <summary>
    /// The class that can provide a Wpf Image Source to display the video.
    /// </summary>
    public class VlcVideoSourceProvider : INotifyPropertyChanged, IDisposable
    {
#if NET45

        /// <summary>
        /// The memory mapped file that contains the picture data
        /// </summary>
        private MemoryMappedFile memoryMappedFile;

        /// <summary>
        /// The view that contains the pointer to the buffer that contains the picture data
        /// </summary>
        private MemoryMappedViewAccessor memoryMappedView;

#else
        /// <summary>
        /// The memory mapped file handle that contains the picture data
        /// </summary>
        private IntPtr memoryMappedFile;

        /// <summary>
        /// The pointer to the buffer that contains the picture data
        /// </summary>
        private IntPtr memoryMappedView;
#endif
        private bool isAlphaChannelEnabled;

        private ImageSource videoSource;

        private Dispatcher _dispatcher;

        private void InvokeOnUI(Action action)
        {
            _dispatcher.Invoke(action);
        }

        private object _callbacks;

        public VlcVideoSourceProvider(vlc.MediaPlayer player, Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            MediaPlayer = player;
            var cleanup = new vlc.MediaPlayer.LibVLCVideoCleanupCb(CleanupVideo);
            var fmt = new vlc.MediaPlayer.LibVLCVideoFormatCb(VideoFormat);
            MediaPlayer.SetVideoFormatCallbacks(fmt, cleanup);
            var lv = new vlc.MediaPlayer.LibVLCVideoLockCb(LockVideo);
            var uv = new vlc.MediaPlayer.LibVLCVideoUnlockCb(UnlockVideo);
            var display = new vlc.MediaPlayer.LibVLCVideoDisplayCb(DisplayVideo);

            MediaPlayer.SetVideoCallbacks(lv, uv, display);

            //reference delegates so GC don't collect them
            _callbacks = new object[] { cleanup, fmt, lv, uv, display };
        }

        /// <summary>
        /// The Image source that represents the video.
        /// </summary>
        public ImageSource VideoSource
        {
            get
            {
                return this.videoSource;
            }

            private set
            {
                if (!Object.ReferenceEquals(this.videoSource, value))
                {
                    this.videoSource = value;
                    this.OnPropertyChanged(nameof(VideoSource));
                }
            }
        }

        /// <summary>
        /// The media player instance. You must call <see cref="CreatePlayer"/> before using this.
        /// </summary>
        public vlc.MediaPlayer MediaPlayer { get; private set; }

        /// <summary>
        /// Defines if <see cref="VideoSource"/> pixel format is <see cref="PixelFormats.Bgr32"/> or <see cref="PixelFormats.Bgra32"/>
        /// </summary>
        public bool IsAlphaChannelEnabled
        {
            get
            {
                return this.isAlphaChannelEnabled;
            }

            set
            {
                this.isAlphaChannelEnabled = value;
            }
        }

        /// <summary>
        /// Aligns dimension to the next multiple of mod
        /// </summary>
        /// <param name="dimension">The dimension to be aligned</param>
        /// <param name="mod">The modulus</param>
        /// <returns>The aligned dimension</returns>
        private uint GetAlignedDimension(uint dimension, uint mod)
        {
            var modResult = dimension % mod;
            if (modResult == 0)
            {
                return dimension;
            }

            return dimension + mod - (dimension % mod);
        }

        IntPtr _buffer;
        int _bufferSize;
        

        #region Vlc video callbacks

        /// <summary>
        /// Called by vlc when the video format is needed. This method allocats the picture buffers for vlc and tells it to set the chroma to RV32
        /// </summary>
        /// <param name="userdata">The user data that will be given to the <see cref="LockVideo"/> callback. It contains the pointer to the buffer</param>
        /// <param name="chroma">The chroma</param>
        /// <param name="width">The visible width</param>
        /// <param name="height">The visible height</param>
        /// <param name="pitches">The buffer width</param>
        /// <param name="lines">The buffer height</param>
        /// <returns>The number of buffers allocated</returns>
        private uint VideoFormat(ref IntPtr userdata, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
        {
            var pixelFormat = IsAlphaChannelEnabled ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            ToFourCC("RV32", chroma);

            pitches = this.GetAlignedDimension((uint)(width * pixelFormat.BitsPerPixel) / 8, 32);
            lines = this.GetAlignedDimension(height, 32);

            var size = pitches * lines;

            this.memoryMappedFile = MemoryMappedFile.CreateNew(null, size);
            var handle = this.memoryMappedFile.SafeMemoryMappedFileHandle.DangerousGetHandle();

            //_bufferSize = (int)size;
            //_buffer = Marshal.AllocHGlobal(_bufferSize*2);
            var args = new
            {
                width = width,
                height = height,
                pixelFormat = pixelFormat,
                pitches = pitches
            };

            InvokeOnUI(() =>
            {
                var bmp = (InteropBitmap)Imaging.CreateBitmapSourceFromMemorySection(handle,
                        (int)args.width, (int)args.height, args.pixelFormat, (int)args.pitches, 0);
                 VideoSource = bmp;
            });

            this.memoryMappedView = memoryMappedFile.CreateViewAccessor();
            var viewHandle = this.memoryMappedView.SafeMemoryMappedViewHandle.DangerousGetHandle();

            userdata = viewHandle;

            return 1;
        }

        /// <summary>
        /// Called by Vlc when it requires a cleanup
        /// </summary>
        /// <param name="userdata">The parameter is not used</param>
        private void CleanupVideo(ref IntPtr userdata)
        {
            // This callback may be called by Dispose in the Dispatcher thread, in which case it deadlocks if we call RemoveVideo again in the same thread.
            if (!disposedValue)
            {
                InvokeOnUI(RemoveVideo);
            }
        }

        /// <summary>
        /// Called by libvlc when it wants to acquire a buffer where to write
        /// </summary>
        /// <param name="userdata">The pointer to the buffer (the out parameter of the <see cref="VideoFormat"/> callback)</param>
        /// <param name="planes">The pointer to the planes array. Since only one plane has been allocated, the array has only one value to be allocated.</param>
        /// <returns>The pointer that is passed to the other callbacks as a picture identifier, this is not used</returns>
        private IntPtr LockVideo(IntPtr opaque, IntPtr planes)
        {
            Marshal.WriteIntPtr(planes, opaque);
            return opaque;
        }

        void UnlockVideo(IntPtr opaque, IntPtr picture, IntPtr planes)
        {

        }

        /// <summary>
        /// Called by libvlc when the picture has to be displayed.
        /// </summary>
        /// <param name="userdata">The pointer to the buffer (the out parameter of the <see cref="VideoFormat"/> callback)</param>
        /// <param name="picture">The pointer returned by the <see cref="LockVideo"/> callback. This is not used.</param>
        private void DisplayVideo(IntPtr userdata, IntPtr picture)
        {
            // Invalidates the bitmap
            InvokeOnUI(() => (VideoSource as InteropBitmap)?.Invalidate());
        }

        #endregion Vlc video callbacks

        /// <summary>
        /// Removes the video (must be called from the Dispatcher thread)
        /// </summary>
        private void RemoveVideo()
        {
            this.VideoSource = null;
           // this.memoryMappedView?.Dispose();
            this.memoryMappedView = null;
           // this.memoryMappedFile?.Dispose();
            this.memoryMappedFile = null;
        }

        #region IDisposable Support

        private bool disposedValue = false;

        /// <summary>
        /// Disposes the control.
        /// </summary>
        /// <param name="disposing">The parameter is not used.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;
                this.MediaPlayer?.Dispose();
                //this.MediaPlayer = null;
                InvokeOnUI(RemoveVideo);
            }
        }

        /// <summary>
        /// The destructor
        /// </summary>
        ~VlcVideoSourceProvider()
        {
            Dispose(false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion IDisposable Support

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public static void ToFourCC(string fourCCString, IntPtr destination)
        {
            if (fourCCString.Length != 4)
            {
                throw new ArgumentException("4CC codes must be 4 characters long", nameof(fourCCString));
            }

            var bytes = Encoding.ASCII.GetBytes(fourCCString);

            for (var i = 0; i < 4; i++)
            {
                Marshal.WriteByte(destination, i, bytes[i]);
            }
        }
    }
}