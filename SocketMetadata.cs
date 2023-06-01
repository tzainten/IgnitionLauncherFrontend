using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace IgnitionLauncherFrontend;

public struct SocketMetadata
{
    public byte[] Data;
    public SocketType Type;
}