using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace TTLibrary
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Input .model required!");
                Console.Read();
                return;
            }

            foreach (var argument in args) {
                Console.WriteLine($"Handling argument {argument}");
                Program.HandleFile(argument);
            }
        }

        static void HandleFile(string path) {
            Console.WriteLine($"\tHandling path {path}");
            FileAttributes attributes = File.GetAttributes(path);

            switch (attributes)
            {
                case FileAttributes.Directory:
                    if (Directory.Exists(path)) {
                        Console.WriteLine($"\t\tDirectory start");
                        string[] files = Directory.GetFiles(path, "*.MODEL", SearchOption.AllDirectories);
                        foreach (var file in files) {
                            Program.HandleFile(file);
                        }
                        Console.WriteLine($"\t\tDirectory done");
                    }
                    else
                        Console.WriteLine($"Directory {path} does not exist.");
                    break;
                default:
                    if (File.Exists(path)) {
                        Console.WriteLine($"Trying file {path}");
                        if (!path.ToLower().EndsWith(".model"))
                        {
                            Console.WriteLine($"Input file must be a .MODEL.");
                            return;
                        }

                        try {
                            Model model = new Model(path);
                        }
                        catch (Exception e) {
                            
                        }
                    }
                    else
                        Console.WriteLine("This file does not exist.");
                    break;
            }
        }
    }
}
