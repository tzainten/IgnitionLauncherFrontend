using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Wpf.Ui.Controls;
using Wpf.Ui.Common;
using System.Net;
using System.Threading;
using System.ComponentModel;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Windows.Threading;

namespace IgnitionLauncherFrontend;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : UiWindow
{
    Dictionary<string, string> Config = new();

    GameState State = GameState.CanPlay;

    SocketMetadata LastSocketMetadata;
    SocketType LastSocketType = SocketType.None;
    int LastFileIndex = 0;
    int LastFolderIndex = 0;

    int FolderCount = 0;
    int FileCount = 0;

    BackgroundWorker FullDownloadThread = new();
    BackgroundWorker UpdateThread = new();
    BackgroundWorker VerificationThread = new();
    BackgroundWorker ConnectionThread = new();
    BackgroundWorker GameWatcherThread = new();

    IgnitionSocket Socket;
#if DEBUGLOCAL
    string ServerAddress = "127.0.0.1";
#else
    string ServerAddress = "67.241.20.18";
#endif
    int ServerPort = 11000;

    static ConcurrentDictionary<string, string> FileMetadata = new();

    public MainWindow()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13;

        InitializeComponent();
        ConfigHandler.Parse();

        TitleBar.CanMaximize = false;
        ProgressText.Visibility = Visibility.Visible;

        PlayBtn.Content = "Connecting";
        PlayBtn.IsEnabled = false;
        PlayBtn.Appearance = ControlAppearance.Secondary;

        DownloadBtn.IsEnabled = false;
        DownloadBtn.Appearance = ControlAppearance.Secondary;

        ConnectionThread.DoWork += new DoWorkEventHandler( Connection_DoWork );
        ConnectionThread.RunWorkerCompleted += new RunWorkerCompletedEventHandler( Connection_WorkerCompleted );

        ConnectionThread.RunWorkerAsync();

        FullDownloadThread.DoWork += new DoWorkEventHandler( FullDownload_DoWork );
        FullDownloadThread.RunWorkerCompleted += new RunWorkerCompletedEventHandler( FullDownload_WorkerCompleted );

        VerificationThread.DoWork += new DoWorkEventHandler( Verification_DoWork );
        VerificationThread.RunWorkerCompleted += new RunWorkerCompletedEventHandler( Verification_WorkerCompleted );

        GameWatcherThread.DoWork += new DoWorkEventHandler( GameWatcher_DoWork );
        GameWatcherThread.RunWorkerCompleted += new RunWorkerCompletedEventHandler( GameWatcher_WorkerCompleted );

        //VerificationThread.RunWorkerAsync();

        UpdateThread.DoWork += new DoWorkEventHandler( Update_DoWork );
        UpdateThread.RunWorkerCompleted += new RunWorkerCompletedEventHandler( Update_WorkerCompleted );
    }

    public static byte[] GetMD5Hash( byte[] data )
    {
        return MD5.Create().ComputeHash( data );
    }

    public static string GetMD5String( byte[] md5 )
    {
        return BitConverter.ToString( md5 ).Replace( "-", string.Empty ).ToLowerInvariant();
    }

    public void FullDownload_DoWork( object? sender, DoWorkEventArgs e )
    {
        string gameDirectory = $@"{ConfigHandler.GetValue( "GAME_ROOT_DIRECTORY" )}\ProjectRogue";

        if ( !Directory.Exists( gameDirectory ) )
            Directory.CreateDirectory( gameDirectory );

        Socket.Write( new byte[ 1 ], SocketType.RequestFullDownload );

        int folderCount = BitConverter.ToInt32( Socket.Read().Data );
        Socket.Close( true );

        for ( int i = 0; i < folderCount; i++ )
        {
            Dispatcher.Invoke( () =>
            {
                ProgressText.Text = $"Creating folder {i + 1}/{folderCount}";
            } );
            Socket.Write( BitConverter.GetBytes( i ), SocketType.RequestDownloadFolder );

            var metadata = Socket.Read();
            Socket.Close( true );

            var folderName = $@"{gameDirectory}\{Encoding.UTF8.GetString( metadata.Data )}";
            if ( Directory.Exists( folderName ) ) continue;

            Directory.CreateDirectory( folderName );
        }

        Socket.Write( new byte[ 1 ], SocketType.RequestFileCount );

        int fileCount = BitConverter.ToInt32( Socket.Read().Data );
        Socket.Close( true );

        for ( int i = 0; i < fileCount; i++ )
        {
            Dispatcher.Invoke( () =>
            {
                ProgressText.Text = $"Downloading file {i + 1} of {fileCount}";
            } );
            Socket.Write( BitConverter.GetBytes( i ), SocketType.RequestDownloadFile );

            var metadata = Socket.Read();
            var filePath = Encoding.UTF8.GetString( metadata.Data );

            string path = string.Empty;
            foreach ( char item in filePath )
            {
                if ( item == '@' ) break;
                path += item;
            }

            Socket.Close( true );

            File.WriteAllBytes( $@"{gameDirectory}\{path}", metadata.Data.Skip( path.Length + 1 ).ToArray() );
        }
    }

    public void FullDownload_WorkerCompleted( object? sender, RunWorkerCompletedEventArgs e )
    {
        PlayBtn.IsEnabled = true;
        PlayBtn.Content = "Play";
        PlayBtn.Appearance = ControlAppearance.Primary;

        DownloadBtn.IsEnabled = false;
        DownloadBtn.Appearance = ControlAppearance.Secondary;

        ProgressText.Text = "";
    }

    public void Verification_DoWork( object? sender, DoWorkEventArgs e )
    {
        Dispatcher.Invoke( () =>
        {
            ProgressText.Text = $"Checking for update";
        } );

        string gameDirectory = $@"{ConfigHandler.GetValue( "GAME_ROOT_DIRECTORY" )}\ProjectRogue";

        string[] files = Directory.GetFiles( gameDirectory, "*", SearchOption.AllDirectories ).Where( name => !name.EndsWith( ".log", StringComparison.OrdinalIgnoreCase ) ).ToArray();
        string[] folders = Directory.GetDirectories( gameDirectory, "*", SearchOption.AllDirectories );

        Parallel.ForEach( files, ( string item ) =>
        {
            var file = File.ReadAllBytes( item );
            FileMetadata.TryAdd( item.Replace( $@"{gameDirectory}\", string.Empty ), GetMD5String( GetMD5Hash( file ) ) );
        } );

        SocketMetadata metadata;
        for ( int i = 0; i < folders.Length; i++ )
        {
            string folder = folders[ i ];
            Dispatcher.Invoke( () =>
            {
                ProgressText.Text = $"Verifying folder {i + 1} of {folders.Length}";
            } );

            byte[] folderPath = Encoding.UTF8.GetBytes( folder.Replace( $@"{gameDirectory}\", string.Empty ) );
            Socket.Write( folderPath, SocketType.AckFolder );
            Socket.Close( true );
        }

        Socket.Write( new byte[ 1 ], SocketType.DoneAckingFolders );
        metadata = Socket.Read();

        if ( metadata.Type == SocketType.NotifyOfMissingFolders )
        {
            State = GameState.Outdated;
            Dispatcher.Invoke( () =>
            {
                PlayBtn.Content = "Please Update";
                DownloadBtn.IsEnabled = true;
                DownloadBtn.Appearance = ControlAppearance.Primary;
            } );
            LastSocketMetadata = metadata;

            Socket.Close( true );
            return;
        }
        Socket.Close( true );

        for ( int i = 0; i < files.Length; i++ )
        {
            string item = files[ i ];
            if ( item.Contains( ".log" ) ) continue;

            string hash;
            if ( !FileMetadata.TryGetValue( item.Replace( $@"{gameDirectory}\", string.Empty ), out hash ) )
                throw new Exception( "Failed to get a hash!" );

            byte[] filePath = Encoding.UTF8.GetBytes( ( item + "@" ).Replace( $@"{gameDirectory}\", string.Empty ) );
            byte[] fileHash = Encoding.UTF8.GetBytes( hash );

            Socket.Write( filePath.Concat( fileHash ).ToArray(), SocketType.CompareFileHash );

            metadata = Socket.Read();
            if ( metadata.Type == SocketType.FileMismatched )
            {
                State = GameState.Outdated;
                Dispatcher.Invoke( () =>
                {
                    var color = new Color();
                    color.R = 255;
                    color.G = 255;
                    color.B = 255;
                    color.A = 255;

                    PlayBtn.Content = "Please Update";
                    DownloadBtn.IsEnabled = true;
                    DownloadBtn.Appearance = ControlAppearance.Primary;
                    DownloadBtn.IconForeground = new SolidColorBrush( color );
                } );
                LastSocketMetadata = metadata;
                LastFileIndex = i;

                Socket.Close( true );
                return;
            }

            Socket.Close( true );
        }

        Socket.Write( new byte[ 1 ], SocketType.DoneComparingFileHashes );

        metadata = Socket.Read();
        Socket.Close();

        if ( metadata.Type == SocketType.NotifyOfMissingFiles )
        {
            State = GameState.Outdated;
            Dispatcher.Invoke( () =>
            {
                var color = new Color();
                color.R = 255;
                color.G = 255;
                color.B = 255;
                color.A = 255;

                PlayBtn.Content = "Please Update";
                DownloadBtn.IsEnabled = true;
                DownloadBtn.Appearance = ControlAppearance.Primary;
                DownloadBtn.IconForeground = new SolidColorBrush( color );
            } );
            LastSocketMetadata = metadata;

            return;
        }

        State = GameState.CanPlay;

        Dispatcher.Invoke( () =>
        {
            PlayBtn.Content = "Play";
            PlayBtn.Appearance = ControlAppearance.Primary;
            PlayBtn.IsEnabled = true;

            DownloadBtn.IsEnabled = false;
            DownloadBtn.Appearance = ControlAppearance.Secondary;
        } );
    }

    public void Connection_DoWork( object? sender, DoWorkEventArgs e )
    {
        Socket = new( ServerAddress, ServerPort );
    }

    public void Connection_WorkerCompleted( object? sender, RunWorkerCompletedEventArgs e )
    {
        ConfigHandler.Parse();

        string gameDirectory = $@"{ConfigHandler.GetValue( "GAME_ROOT_DIRECTORY" )}\ProjectRogue";

        bool validDirectory = Directory.Exists( gameDirectory );

        string[] files;

        try
        {
            files = Directory.GetFiles( gameDirectory, "*", SearchOption.AllDirectories ).Where( name => !name.EndsWith( ".log", StringComparison.OrdinalIgnoreCase ) ).ToArray();
        }
        catch ( Exception _ )
        {
            files = new string[] { };
        }

        if ( !validDirectory || files.Length == 0 )
        {
            PlayBtn.Appearance = ControlAppearance.Secondary;
            PlayBtn.IsEnabled = false;
            PlayBtn.Content = "Not Installed";

            var color = new Color();
            color.R = 255;
            color.G = 255;
            color.B = 255;
            color.A = 255;

            DownloadBtn.Appearance = ControlAppearance.Primary;
            DownloadBtn.IsEnabled = true;
            DownloadBtn.IconForeground = new SolidColorBrush( color );

            State = GameState.NotInstalled;
        }
        else if ( validDirectory && files.Length > 0 )
        {
            PlayBtn.Appearance = ControlAppearance.Secondary;
            PlayBtn.IsEnabled = false;
            PlayBtn.Content = "Verifying";

            DownloadBtn.Appearance = ControlAppearance.Secondary;
            DownloadBtn.IsEnabled = false;

            State = GameState.Verifying;
            VerificationThread.RunWorkerAsync();
        }
        else
        {
            PlayBtn.Appearance = ControlAppearance.Primary;
            PlayBtn.IsEnabled = true;
            PlayBtn.Content = "Play";
        }

        PlayBtn.Appearance = PlayBtn.IsEnabled ? ControlAppearance.Primary : ControlAppearance.Secondary;
    }

    public void Verification_WorkerCompleted( object? sender, RunWorkerCompletedEventArgs e )
    {
        ProgressText.Text = "";
    }

    Process Game;
    public void GameWatcher_DoWork( object? sender, DoWorkEventArgs e )
    {
        while ( !Game.HasExited )
        {
            Game.Refresh();
            Thread.Sleep( 10 );
        }
    }

    public void GameWatcher_WorkerCompleted( object? sender, RunWorkerCompletedEventArgs e )
    {
        Dispatcher.Invoke( () =>
        {
            PlayBtn.IsEnabled = true;
        } );
    }

    public void Update_DoWork( object? sender, DoWorkEventArgs e )
    {
        string gameDirectory = $@"{ConfigHandler.GetValue( "GAME_ROOT_DIRECTORY" )}\ProjectRogue";

        string[] files = Directory.GetFiles( gameDirectory, "*", SearchOption.AllDirectories ).Where( name => !name.EndsWith( ".log", StringComparison.OrdinalIgnoreCase ) ).ToArray();
        string[] folders = Directory.GetDirectories( gameDirectory, "*", SearchOption.AllDirectories );

        SocketMetadata metadata = LastSocketMetadata;
        switch ( metadata.Type )
        {
            case SocketType.NotifyOfMissingFiles:
                {
                    goto MissingFiles;
                }
        }

        if ( metadata.Type == SocketType.NotifyOfMissingFolders )
        {
            int missingFolderCount = BitConverter.ToInt32( metadata.Data );
            for ( int i = LastFolderIndex; i < missingFolderCount; i++ )
            {
                Dispatcher.Invoke( () =>
                {
                    ProgressText.Text = $"Creating folder {i + 1} of {missingFolderCount}";
                } );

                Socket.Write( new byte[ 1 ], SocketType.RequestMissingFolder );

                metadata = Socket.Read();
                Socket.Close( true );

                var folderPath = $@"{gameDirectory}\{Encoding.UTF8.GetString( metadata.Data )}";

                if ( Directory.Exists( folderPath ) ) continue;
                Directory.CreateDirectory( folderPath );
            }
        }

        for ( int i = LastFileIndex; i < files.Length; i++ )
        {
            string item = files[ i ];

            Dispatcher.Invoke( () =>
            {
                ProgressText.Text = $"Downloading file {i} of {files.Length - LastFileIndex - 1}";
            } );

            string hash;
            if ( !FileMetadata.TryGetValue( item.Replace( $@"{gameDirectory}\", string.Empty ), out hash ) )
                throw new Exception( "Failed to get a hash!" );

            byte[] filePath = Encoding.UTF8.GetBytes( ( item + "@" ).Replace( $@"{gameDirectory}\", string.Empty ) );
            byte[] fileHash = Encoding.UTF8.GetBytes( hash );

            Socket.Write( filePath.Concat( fileHash ).ToArray(), SocketType.CompareFileHash );

            metadata = Socket.Read();
            if ( metadata.Type == SocketType.FileMismatched )
            {
                File.WriteAllBytes( item, metadata.Data );
            }

            Socket.Close( true );
        }

        Socket.Write( new byte[ 1 ], SocketType.DoneComparingFileHashes );

        metadata = Socket.Read();

    MissingFiles:
        if ( metadata.Type == SocketType.NotifyOfMissingFiles )
        {
            int missingFileCount = BitConverter.ToInt32( metadata.Data );
            for ( int i = 0; i < missingFileCount; i++ )
            {
                Socket.Close( true );
                Socket.Write( new byte[ 1 ], SocketType.RequestMissingFile );

                metadata = Socket.Read();
                var filePath = Encoding.UTF8.GetString( metadata.Data );

                string path = string.Empty;
                foreach ( char item in filePath )
                {
                    if ( item == '@' )
                    {
                        Socket.Close();
                        break;
                    }
                    path += item;
                }

                File.WriteAllBytes( $@"{gameDirectory}\{path}", metadata.Data.Skip( path.Length + 1 ).ToArray() );
            }
        }
    }

    public void Update_WorkerCompleted( object? sender, RunWorkerCompletedEventArgs e )
    {
        State = GameState.CanPlay;

        Dispatcher.Invoke( () =>
        {
            PlayBtn.Content = "Play";
            PlayBtn.Appearance = ControlAppearance.Primary;
            PlayBtn.IsEnabled = true;

            DownloadBtn.IsEnabled = false;
            DownloadBtn.Appearance = ControlAppearance.Secondary;
        } );
    }

    public void DownloadClick( object sender, RoutedEventArgs e )
    {
        DownloadBtn.IsEnabled = false;

        switch ( State )
        {
            case GameState.NotInstalled:
                {
                    if ( !Directory.Exists( ConfigHandler.GetValue( "GAME_ROOT_DIRECTORY" ) ) )
                    {
                        FolderBrowserDialog dialog = new();

                        if ( dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK )
                        {
                            ProgressText.Visibility = Visibility.Visible;
                            ConfigHandler.SetValue( "GAME_ROOT_DIRECTORY", dialog.SelectedPath );
                        }
                    }

                    FullDownloadThread.RunWorkerAsync();
                    break;
                }
            case GameState.Outdated:
                {
                    UpdateThread.RunWorkerAsync();

                    break;
                }
        }
    }

    public void PlayClick( object sender, RoutedEventArgs e )
    {
        string exePath = $@"{ConfigHandler.GetValue( "GAME_ROOT_DIRECTORY" )}\ProjectRogue\Windows\Construct.exe";
        Game = Process.Start( exePath );
        PlayBtn.IsEnabled = false;

        GameWatcherThread.RunWorkerAsync();
    }
}