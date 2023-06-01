using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IgnitionLauncherFrontend;

public enum SocketType : int
{
    None = 0,
    CompareFileHash,
    RequestFullDownload,
    RequestDownloadFolder,
    RequestFileCount,
    RequestDownloadFile,
    FileMismatched,
    DoneComparingFileHashes,
    AckFolder,
    DoneAckingFolders,
    NotifyOfMissingFolders,
    NotifyOfMissingFiles,
    RequestMissingFile,
    RequestMissingFolder
}