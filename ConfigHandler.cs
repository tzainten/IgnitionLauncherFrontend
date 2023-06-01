using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shapes;

namespace IgnitionLauncherFrontend;

public static class ConfigHandler
{
    public static Dictionary<string, ConfigPairMetadata> Config = new();
    private static string _path = @$"{Environment.CurrentDirectory}\\Config.txt";

    static ConfigHandler()
    {
        if ( !File.Exists( _path ) )
            File.WriteAllText( _path, "GAME_ROOT_DIRECTORY=" );
    }

    public static void Parse()
    {
        int lineNumber = 0;
        string[] lines = File.ReadAllLines( _path );
        foreach ( string line in lines )
        {
            string[] pair = line.Split( '=' );
            if ( Config.ContainsKey( pair[ 0 ] ) ) break;
            Config.Add( pair[ 0 ], new()
            {
                Key = pair[ 0 ],
                Value = pair[ 1 ],
                LineNumber = lineNumber
            } );
        }
    }

    public static string GetValue( string key )
    {
        return Config[ key ].Value;
    }

    public static void SetValue( string key, string value )
    {
        string[] lines = File.ReadAllLines( _path );

        int lineNumber = Config[ key ].LineNumber;
        string[] line = lines[ lineNumber ].Split( '=' );
        line[ 1 ] = value;

        lines[ lineNumber ] = line[ 0 ] + "=" + line[ 1 ];
        File.WriteAllLines( _path, lines );
    }
}