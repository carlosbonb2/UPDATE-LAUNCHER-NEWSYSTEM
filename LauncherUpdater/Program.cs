using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace LauncherUpdater
{
    internal class Program
    {
        const string NOME_UPDATER_TEMPORARIO = "LauncherUpdater.tmp";
        static string _caminhoLogDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RELATORIO_UPDATER.txt");

        static void Main(string[] args)
        {
            try
            {
                // Move o contexto para o Temp para evitar bloquear a própria pasta
                Directory.SetCurrentDirectory(Path.GetTempPath());
                LimparUpdaterTemporario();

                Narrar($"--- INICIO DA ATUALIZAÇÃO (V2.0): {DateTime.Now} ---");
                Narrar($"Args: {string.Join(" ", args)}");

                if (args.Length == 0 || args.Contains("--fix-mode")) return;

                ExecutarAtualizacaoAgnostica(args);
            }
            catch (Exception ex)
            {
                Narrar("\n!!! ERRO CRÍTICO !!!\n" + ex.ToString());
                Console.WriteLine("ERRO FATAL. Pressione ENTER...");
                try { Console.ReadLine(); } catch { }
            }
        }

        private static void ExecutarAtualizacaoAgnostica(string[] args)
        {
            // Extração dos argumentos passados pelo Launcher
            int parentPid = int.Parse(GetArg(args, "--pid", "0"));
            string zipPath = GetArg(args, "--zip-path");
            string targetDir = GetArg(args, "--target-dir");
            string exeNameFinal = GetArg(args, "--exe-name");
            string mutexName = GetArg(args, "--mutex");

            // Pega os arquivos que não podem ser sobrescritos (separados por pipe '|')
            string[] arquivosPreservados = GetArg(args, "--preserve").Split('|', StringSplitOptions.RemoveEmptyEntries);

            Narrar($"Alvo: {targetDir} | Preservando: {arquivosPreservados.Length} itens");

            // 1. ASSASSINATO DE PRECISÃO (PID + MUTEX)
            GarantirMorteDoProcesso(parentPid, mutexName);
            MatarProcessosNaPasta(targetDir);
            Thread.Sleep(1500);

            // 2. EXTRAÇÃO DIRETA COM PRESERVAÇÃO
            Narrar("Iniciando injeção de arquivos...");
            AplicarAtualizacaoCirurgica(zipPath, targetDir, arquivosPreservados);

            // 3. REINÍCIO
            string caminhoExeFinal = Path.Combine(targetDir, exeNameFinal);
            if (File.Exists(caminhoExeFinal))
            {
                Narrar($"Sucesso! Reiniciando: {caminhoExeFinal}");
                Process.Start(new ProcessStartInfo(caminhoExeFinal) { WorkingDirectory = targetDir, UseShellExecute = true });
            }
            else
            {
                throw new FileNotFoundException("O executável principal sumiu!");
            }
        }

        private static void AplicarAtualizacaoCirurgica(string zipPath, string targetDir, string[] preservados)
        {
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string destinoCompleto = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));

                    // Se for uma pasta, apenas cria
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destinoCompleto);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destinoCompleto)!);

                    // Verifica se o arquivo atual está na lista VIP de preservação
                    bool devePreservar = preservados.Any(p => entry.Name.Equals(p, StringComparison.OrdinalIgnoreCase));

                    if (devePreservar && File.Exists(destinoCompleto))
                    {
                        Narrar($"[BLINDADO] Preservando arquivo local: {entry.Name}");
                        continue;
                    }

                    // Tenta extrair com Retry (Anti-Vírus as vezes trava o arquivo por milissegundos)
                    bool sucesso = false;
                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            entry.ExtractToFile(destinoCompleto, true);
                            sucesso = true;
                            break;
                        }
                        catch { Thread.Sleep(500); }
                    }
                    if (!sucesso) throw new Exception($"Falha ao extrair (Arquivo travado): {entry.Name}");
                }
            }
        }

        private static void GarantirMorteDoProcesso(int pid, string mutexName)
        {
            if (pid > 0)
            {
                try
                {
                    Process p = Process.GetProcessById(pid);
                    p.Kill();
                    p.WaitForExit(2000);
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(mutexName))
            {
                Narrar("Aguardando liberação do Mutex Genético...");
                for (int i = 0; i < 10; i++)
                {
                    if (Mutex.TryOpenExisting(mutexName, out _)) Thread.Sleep(500);
                    else break;
                }
            }
        }

        static void MatarProcessosNaPasta(string dir)
        {
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == Environment.ProcessId) continue;
                        if (p.MainModule?.FileName.StartsWith(dir, StringComparison.OrdinalIgnoreCase) == true)
                            p.Kill();
                    }
                    catch { }
                }
            }
            catch { }
        }

        static void LimparUpdaterTemporario()
        {
            try { string tmp = Path.Combine(AppContext.BaseDirectory, NOME_UPDATER_TEMPORARIO); if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }

        static void Narrar(string texto)
        {
            try { File.AppendAllText(_caminhoLogDesktop, $"[{DateTime.Now:HH:mm:ss}] {texto}\n"); Console.WriteLine(texto); } catch { }
        }

        static string GetArg(string[] args, string key, string fallback = "")
        {
            string prefix = key + "=";
            var arg = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return arg != null ? arg.Substring(prefix.Length).Trim('"') : fallback;
        }
    }
}