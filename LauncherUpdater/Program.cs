using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace LauncherUpdater
{
    internal class Program
    {
        // Definição dos nomes para a transição
        const string NOME_ANTIGO_ERRADO = "PDV_Laucher.exe";
        const string NOME_NOVO_CORRETO = "PDV_Launcher.exe";

        static void Main(string[] args)
        {
            try
            {
                if (args.Length < 4) return;

                var argsDict = args.Select(s => s.Split(new[] { '=' }, 2))
                                   .Where(p => p.Length == 2)
                                   .ToDictionary(a => a[0], a => a[1]);

                int parentPid = int.Parse(argsDict["--parent-pid"]);
                string sourceDir = argsDict["--source-dir"];
                string targetDir = argsDict["--target-dir"];
                string exeNameSolicitado = argsDict["--exe-name"]; // O Launcher antigo vai mandar o nome errado aqui

                // 1. Espera o Launcher morrer
                try
                {
                    Process parent = Process.GetProcessById(parentPid);
                    parent.WaitForExit(10000);
                }
                catch { }

                Thread.Sleep(1000);

                // 2. Atualiza os arquivos (Copia o novo PDV_Launcher.exe para a pasta)
                CopyDirectory(sourceDir, targetDir);

                // 3. LIMPEZA DO LEGADO (A Mágica acontece aqui)
                string caminhoAntigo = Path.Combine(targetDir, NOME_ANTIGO_ERRADO);
                string caminhoNovo = Path.Combine(targetDir, NOME_NOVO_CORRETO);

                // Se existir o arquivo velho, apagamos ele para não ficar lixo
                if (File.Exists(caminhoAntigo) && File.Exists(caminhoNovo))
                {
                    try
                    {
                        Console.WriteLine("Removendo executável com nome antigo...");
                        File.Delete(caminhoAntigo);
                    }
                    catch { }
                }

                // 4. Lógica de Correção de Inicialização
                string exeParaIniciar = Path.Combine(targetDir, exeNameSolicitado);

                // Se o Launcher antigo pediu para abrir o "Laucher" (sem N), mas ele não existe mais...
                if (!File.Exists(exeParaIniciar) &&
                    exeNameSolicitado.Equals(NOME_ANTIGO_ERRADO, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Detectada migração de versão. Redirecionando para o novo executável...");
                    // ...trocamos para o "Launcher" (com N)
                    exeParaIniciar = caminhoNovo;
                }

                // 5. Iniciar o novo Launcher
                if (File.Exists(exeParaIniciar))
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo(exeParaIniciar)
                    {
                        WorkingDirectory = targetDir,
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }
                else
                {
                    // Fallback de emergência: Tenta achar qualquer .exe que pareça o launcher
                    string[] candidatos = Directory.GetFiles(targetDir, "PDV_Lau*.exe");
                    if (candidatos.Length > 0)
                    {
                        Process.Start(new ProcessStartInfo(candidatos[0]) { WorkingDirectory = targetDir, UseShellExecute = true });
                    }
                }
            }
            catch (Exception ex)
            {
                string log = Path.Combine(Path.GetTempPath(), "pdv_update_error.txt");
                File.WriteAllText(log, ex.ToString());
            }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(targetDir, new DirectoryInfo(subDir).Name);
                CopyDirectory(subDir, destSubDir);
            }
        }
    }
}