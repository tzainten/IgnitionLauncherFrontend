using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace IgnitionLauncherFrontend;

public class IgnitionSocket
{
    private TcpClient _client;
    private NetworkStream _stream;

    private string _hostname = string.Empty;
    private int _port;

    public IgnitionSocket( TcpClient client )
    {
        _client = client;
        _stream = client.GetStream();
    }

    public IgnitionSocket( string hostname, int port )
    {
        _hostname = hostname;
        _port = port;

        while ( _client is null )
        {
            try
            {
                _client = new( hostname, port );
                _stream = _client.GetStream();
            }
            catch ( Exception ex ) { Debug.WriteLine( ex.Message ); }
        }
    }

    public void Write( string input, SocketType packetType = SocketType.None ) => Write( Encoding.UTF8.GetBytes( input ), packetType );

    public void Write( byte[] data, SocketType packetType = SocketType.None )
    {
        byte[] length = BitConverter.GetBytes( data.Length );
        byte[] type = BitConverter.GetBytes( ( int )packetType );

        string base64 = Convert.ToBase64String( data );
        byte[] base64Bytes = length.Concat( type ).Concat( Convert.FromBase64String( base64 ) ).ToArray();

        _stream.Write( base64Bytes, 0, base64Bytes.Length );
    }

    public SocketMetadata Read()
    {
        List<byte> bufferList = new();

        byte[] lengthBuffer = new byte[ 4 ];

        int bytesRead = 0;
        do
        {
            bytesRead += _stream.Read( lengthBuffer, 0, lengthBuffer.Length );
        }
        while ( bytesRead < 4 );

        byte[] typeBuffer = new byte[ 4 ];

        bytesRead = 0;
        do
        {
            bytesRead += _stream.Read( typeBuffer, 0, typeBuffer.Length );
        }
        while ( bytesRead < 4 );

        int dataLength = BitConverter.ToInt32( lengthBuffer );
        SocketType packetType = ( SocketType )BitConverter.ToInt32( typeBuffer );

        byte[] buffer = new byte[ _client.ReceiveBufferSize ];

        int totalBytesRead = 0;
        do
        {
            bytesRead = _stream.Read( buffer, 0, buffer.Length );
            totalBytesRead += bytesRead;

            for ( int i = 0; i < bytesRead; i++ )
                bufferList.Add( buffer[ i ] );
        } while ( totalBytesRead < dataLength );

        return new SocketMetadata()
        {
            Data = Convert.FromBase64String( Convert.ToBase64String( bufferList.ToArray() ) ),
            Type = packetType
        };
    }

    public void Close( bool establishNewConnection = false )
    {
        _stream.Close();
        _client.Close();

        if ( establishNewConnection )
        {
            _client = new( _hostname, _port );
            _stream = _client.GetStream();
        }
    }
}