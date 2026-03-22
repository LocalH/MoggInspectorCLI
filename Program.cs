using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.IO;
using System.IO.Enumeration;
using CommandLine;
using CommandLine.Text;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using MoggInspectorLib;

namespace MoggInspectorCLI
{
    internal class Program
    {
        public class Options
        {
            [Option('q', "quiet", SetName = "conout", Required = false, HelpText = "Only output PS3 keymask mismatches and red key MOGG detection to stdout")]
            public bool Quiet { get; set; }

            [Option('v', "verbose", SetName = "conout", Required = false, HelpText = "Display more information about the MOGG header")]
            public bool Verbose { get; set; }

            [Option('p', "patch", Required = false, HelpText = "Patch PS3 keymask if it generates a mismatching key")]
            public bool Patch { get; set; }

            [Option('f', "files", Required = true, HelpText = "MOGGs to be inspected")]
            public required string MoggFiles { get; set; }
        }

        static void ProcessMogg(string moggFile, Options opts)
        {
            bool RedKeyMogg = false;
            bool DidPrint = false;
            uint OggOffset;
            byte[] MoggHeader;

            using (var stream = File.Open(moggFile, FileMode.Open))
            {
                using (var reader = new BinaryReader(stream))
                {
                    stream.Seek(4, SeekOrigin.Begin);
                    OggOffset = reader.ReadUInt32();
                    stream.Seek(0, SeekOrigin.Begin);
                    MoggHeader = reader.ReadBytes((int)(OggOffset));
                    stream.Close();
                }
            }

            var kc = new KeyChain();
            kc.DeriveKeys(MoggHeader, false);
            uint Ps3MaskOffset = kc.Ps3MaskOffset;
            byte[] Ps3FixedMask = kc.Ps3FixedMask;
            if (kc.KeymaskMismatch)
            {
                kc.DeriveKeys(MoggHeader, true);
                if (kc.XboxAesKey.SequenceEqual(kc.Ps3AesKey))
                {
                    kc.KeymaskMismatch = false;
                    RedKeyMogg = true;
                }
                else
                {
                    kc.DeriveKeys(MoggHeader, false);
                }
            }
            string encVer;
            string encType;
            string subEncVer = "";

            switch (kc.Version)
            {
                case 10:
                    encVer = "10 (0x0A)";
                    encType = "GH2/unencrypted";
                    break;
                case 11:
                    encVer = "11 (0x0B)";
                    if (kc.IsC3Mogg)
                    {
                        encType = "OG C3";
                    }
                    else
                    {
                        encType = "Harmonix RB1";
                    }
                    break;
                case 12:
                    encVer = "12 (0x0C)";
                    if (kc.IsC3Mogg)
                    {
                        encType = "Old C3";
                    }
                    else if (kc.IsNemoMogg)
                    {
                        encType = "Nemo";
                    }
                    else
                    {
                        encType = "Harmonix RB1/RB2";
                    }
                    break;
                case 13:
                    encVer = "13 (0x0D)";
                    if (kc.IsNemoMogg)
                    {
                        encType = "Nemo";
                    }
                    else
                    {
                        encType = "New C3";
                    }
                    break;
                case 14:
                    encVer = "14 (0x0E)";
                    encType = "Harmonix RB1/2";
                    break;
                case 15:
                    encVer = "15 (0x0F)";
                    encType = "Harmonix Rock Band Network";
                    break;
                case 16:
                    encVer = "16 (0x10)";
                    encType = "Harmonix RB3";
                    break;
                case 17:
                    encVer = "17 (0x11)";
                    encType = "Harmonix Forge";
                    break;
                default:
                    encVer = String.Format("{0} (0x{0:X2})", kc.Version);
                    encType = "Unknown";
                    break;
            }

            if (kc.Version == 17)
            {
                subEncVer = kc.V17Keyset switch
                {
                    1 => "1 (Rock Band 4)",
                    4 => "4 (DropMix)",
                    6 => "6 (Dance Central VR)",
                    8 => "8 (Audica)",
                    10 => "10 (FUSER)",
                    _ => String.Format("{0} (unknown)", kc.V17Keyset),
                };
            }

            if (RedKeyMogg || kc.KeymaskMismatch || opts.Verbose || !opts.Quiet)
            {
                Console.WriteLine("Filename: "+moggFile);
                DidPrint = true;
            }

            if (!opts.Quiet)
            {
                Console.WriteLine("Encryption version: " + encVer + " (" + encType + ")");
                if (kc.Version == 17)
                {
                    Console.WriteLine("v17 keyset: " + subEncVer);
                }
                DidPrint = true;
            }

            if (kc.KeymaskMismatch)
            {
                Console.WriteLine("PS3 keymask incorrect!");
                if (!opts.Quiet)
                {
                    Console.WriteLine("Error keymask: " + BitConverter.ToString(kc.Ps3Mask).Replace("-", string.Empty));
                    Console.WriteLine("Fixed keymask: " + BitConverter.ToString(Ps3FixedMask).Replace("-", string.Empty));
                    DidPrint = true;
                }
                if (opts.Patch)
                {
                    if (kc.Version < 18)
                    {
                        if (kc.V17Keyset < 11)
                        {
                            using (var stream = File.Open(moggFile, FileMode.Open))
                            {
                                using (var writer = new BinaryWriter(stream))
                                {
                                    stream.Seek(Ps3MaskOffset, SeekOrigin.Begin);
                                    writer.Write(Ps3FixedMask);
                                    stream.Flush();
                                    stream.Close();
                                }
                            }
                            Console.WriteLine("MOGG has been patched!");
                        }
                        else
                        {
                            Console.WriteLine("MOGG has unknown version or subversion, will not patch!");
                        }
                    }
                    else
                    {
                        Console.WriteLine("MOGG has unknown version or subversion, will not patch!");
                    }
                }
            }

            if (opts.Verbose)
            {
                if (kc.Version > 11)
                {
                    Console.WriteLine("magicA: " + BitConverter.ToString((kc.MagicA).Reverse().ToArray()).Replace("-", string.Empty));
                    Console.WriteLine("magicB: " + BitConverter.ToString((kc.MagicB).Reverse().ToArray()).Replace("-", string.Empty));
                    Console.WriteLine("360 keymask: " + BitConverter.ToString(kc.XboxMaskDec).Replace("-", string.Empty));
                    Console.WriteLine("PS3 keymask: " + BitConverter.ToString(kc.Ps3Mask).Replace("-", string.Empty));
                    Console.WriteLine("360 keyindex: " + kc.XboxIndex.ToString());
                    Console.WriteLine("PS3 keyindex: " + kc.Ps3Index.ToString());
                    DidPrint = true;
                }
                if (kc.Version > 10)
                {
                    Console.WriteLine("Nonce: " + BitConverter.ToString(kc.Nonce).Replace("-", string.Empty));
                    Console.WriteLine("AES key: " + BitConverter.ToString(kc.XboxAesKey).Replace("-", string.Empty));
                    DidPrint = true;
                }
            }

            if (RedKeyMogg)
            {
                Console.WriteLine("THIS MOGG IS A RED KEY MOGG PLEASE CONTACT LOCAL H ON GITHUB OR DISCORD WITH MORE INFORMATION");
                DidPrint = true;
            }

            if (kc.Version > 17)
            {
                Console.WriteLine("This MOGG is marked as encryption v" + kc.Version + ", did Harmonix start using MOGGs again? Please contact LocalH on GitHub or Discord");
                DidPrint = true;
            }

            if (RedKeyMogg || kc.KeymaskMismatch || opts.Verbose || DidPrint )
            {
                Console.WriteLine();
                DidPrint = false;
            }

        }
        static void RunOptions(Options opts)
        {
            string directory = Path.GetDirectoryName(opts.MoggFiles);
            if (directory == "")
            {
                directory = Directory.GetCurrentDirectory();
            }
            string filename = Path.GetFileName(opts.MoggFiles);

            Matcher matcher = new();
            matcher.AddIncludePatterns(new[] { filename });

            IEnumerable<string> matchedFiles = matcher.GetResultsInFullPath(directory);

            foreach (string file in matchedFiles)
            {
                ProcessMogg(file,opts);
            }
        }
        static void Main(string[] args)
        {
            var parser = new Parser(with => with.HelpWriter = null);

            var parserResult = parser.ParseArguments<Options>(args);

            parserResult
                .WithParsed(options => RunOptions(options))
                .WithNotParsed(errs =>
                {
                    var helpText = HelpText.AutoBuild(parserResult, h =>
                    {
                        h.AdditionalNewLineAfterOption = false;
                        h.Heading = "MoggInspectorCLI 1.2a";
                        h.AddPostOptionsText("If this tool detects a red key mogg, please contact LocalH on GitHub or Discord.");
                        return HelpText.DefaultParsingErrorsHandler(parserResult, h);
                    }, e => e);

                    foreach (var err in errs)
                    {
                        if (err is HelpRequestedError || err is VersionRequestedError)
                        {
                            Console.WriteLine(helpText);
                        }
                        else
                        {
                            Console.WriteLine($"Error: {err}");
                            Console.WriteLine(helpText);
                        }
                    }
                }
                );

        }
    }
}
