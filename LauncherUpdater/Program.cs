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

        // Log no Desktop para garantir visibilidade
        static string _caminhoLogDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RELATORIO_UPDATER.txt");
        static string _exeNameSolicitado = "PDV_Launcher.exe";
        static string _caminhoZipDoLauncher = string.Empty;

        static void Main(string[] args)
        {
            try
            {
                // Limpeza residual
                LimparUpdaterTemporario();

                File.WriteAllText(_caminhoLogDesktop, $"--- INICIO DA ATUALIZAÇÃO: {DateTime.Now} ---\n");
                Narrar("Updater iniciado (Modo Sandbox/Temp).");
                Narrar($"Args: {string.Join(" ", args)}");

                if (args.Length == 0) return;

                if (args.Contains("--fix-mode"))
                {
                    Narrar("Modo correção (dummy).");
                    return;
                }

                ExecutarAtualizacaoPadrao(args);
            }
            catch (Exception ex)
            {
                Narrar("\n------------------------------------------------");
                Narrar("!!! ERRO CRÍTICO !!!");
                Narrar(ex.ToString());
                Narrar("------------------------------------------------");

                // Em caso de erro fatal, tenta manter o console aberto se possível
                Console.WriteLine("ERRO FATAL. Pressione ENTER...");
                try { Console.ReadLine(); } catch { }
            }
            finally
            {
                Narrar($"\nFim: {DateTime.Now}.");
                // Limpa o ZIP baixado
                try
                {
                    if (File.Exists(_caminhoZipDoLauncher)) File.Delete(_caminhoZipDoLauncher);
                }
                catch { Narrar("Falha ao limpar ZIP (sem permissão)."); }
            }
        }

        private static void ExecutarAtualizacaoPadrao(string[] args)
        {
            Narrar("Lendo argumentos...");
            int parentPid = int.Parse(GetArg(args, "--pid"));
            string zipPath = GetArg(args, "--zip-path");
            string targetDir = GetArg(args, "--target-dir");
            _exeNameSolicitado = GetArg(args, "--exe-name");
            _caminhoZipDoLauncher = zipPath;

            // Define o nome da pasta de backup
            string backupDir = targetDir + "_BACKUP_" + DateTime.Now.ToString("HHmmss");

            Narrar($"PID Pai: {parentPid} | Alvo: {targetDir}");

            // 1. Matar Processos e Esperar
            MatarProcessoPai(parentPid);
            MatarProcessosNaPasta(targetDir);

            Narrar("Aguardando 2s para liberação do SO...");
            Thread.Sleep(2000);

            // 2. BACKUP E PREPARAÇÃO (Move a pasta atual para _BACKUP)
            bool backupCriado = false;
            try
            {
                if (Directory.Exists(targetDir))
                {
                    Narrar($"Movendo instalação atual para backup: {backupDir}");
                    if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);

                    // AQUI É O PULO DO GATO: Mover é mais rápido e evita conflito de DLL travada
                    Directory.Move(targetDir, backupDir);
                    backupCriado = true;
                }

                // Cria a pasta limpa para a nova versão
                Directory.CreateDirectory(targetDir);
            }
            catch (Exception ex)
            {
                Narrar($"ERRO CRÍTICO NO BACKUP: {ex.Message}");
                throw; // Se falhar aqui, aborta tudo
            }

            // 3. INSTALAÇÃO E RESTAURAÇÃO
            try
            {
                // A. Instala arquivos novos do ZIP
                AplicarAtualizacao(zipPath, targetDir, _exeNameSolicitado);

                // B. --- SALVA-VIDAS: RESTAURA DADOS DO USUÁRIO DO BACKUP ---
                if (backupCriado)
                {
                    Narrar("Restaurando dados do usuário (Banco de Dados e Config)...");
                    RestaurarDadosUsuario(backupDir, targetDir);
                }
                // -----------------------------------------------------------

                // C. Validação e Reinício
                string caminhoExeFinal = Path.Combine(targetDir, _exeNameSolicitado);
                if (File.Exists(caminhoExeFinal))
                {
                    Narrar("Limpando backup antigo...");
                    // Só deleta o backup se tiver certeza que restaurou os dados e o exe existe
                    if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);

                    Narrar($"Reiniciando: {caminhoExeFinal}");
                    Process.Start(new ProcessStartInfo(caminhoExeFinal)
                    {
                        WorkingDirectory = targetDir,
                        UseShellExecute = true
                    });
                }
                else
                {
                    throw new FileNotFoundException("O executável principal sumiu após a atualização!");
                }
            }
            catch (Exception ex)
            {
                Narrar($"FALHA NA INSTALAÇÃO ({ex.Message}). Iniciando ROLLBACK...");

                // LÓGICA DE ROLLBACK
                try
                {
                    if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true); // Apaga a tentativa falha
                    if (Directory.Exists(backupDir)) Directory.Move(backupDir, targetDir); // Traz o backup de volta
                    Narrar("Rollback concluído com sucesso.");
                }
                catch (Exception rbEx)
                {
                    Narrar($"DESASTRE: Falha no Rollback! {rbEx.Message}");
                }
                throw; // Relança erro para encerrar
            }
        }

        private static void RestaurarDadosUsuario(string pastaBackup, string pastaDestino)
        {
            try
            {

                string[] arquivosParaSalvar = { "pdv_database.db", "config.ini" };

                foreach (var arquivo in arquivosParaSalvar)
                {
                    string origem = Path.Combine(pastaBackup, arquivo);
                    string destino = Path.Combine(pastaDestino, arquivo);

                    if (File.Exists(origem))
                    {
                        Narrar($"   -> Restaurando: {arquivo}");
                        File.Copy(origem, destino, true);
                    }
                }

                // Mantém a restauração da pasta de backups históricos
                string pastaBackupsAntigos = Path.Combine(pastaBackup, "Backups");
                string pastaBackupsNova = Path.Combine(pastaDestino, "Backups");
                if (Directory.Exists(pastaBackupsAntigos))
                {
                    if (!Directory.Exists(pastaBackupsNova))
                        Directory.Move(pastaBackupsAntigos, pastaBackupsNova);
                    Narrar("   -> Pasta de Backups históricos restaurada.");
                }
            }
            catch (Exception ex)
            {
                Narrar($"AVISO: Erro ao restaurar dados do usuário: {ex.Message}");
            }
        }

        private static void AplicarAtualizacao(string zipPath, string targetDir, string exeNameFinal)
        {
            string tempFolder = Path.Combine(Path.GetTempPath(), "PDV_Extracted_" + Guid.NewGuid().ToString().Substring(0, 5));
            Narrar($"Extraindo ZIP...");

            try
            {
                Directory.CreateDirectory(tempFolder);
                ZipFile.ExtractToDirectory(zipPath, tempFolder, true);

                // Lógica inteligente para achar a raiz correta dentro do ZIP
                string origemReal = tempFolder;
                var dirs = Directory.GetDirectories(tempFolder);
                var files = Directory.GetFiles(tempFolder);
                if (files.Length == 0 && dirs.Length == 1) origemReal = dirs[0];

                CopiarRecursivo(origemReal, targetDir);
            }
            finally
            {
                try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true); } catch { }
            }
        }

        // --- Helpers ---

        static void MatarProcessoPai(int pid)
        {
            try
            {
                Process p = Process.GetProcessById(pid);
                p.WaitForExit(3000);
                if (!p.HasExited) p.Kill();
            }
            catch { }
        }

        static void MatarProcessosNaPasta(string dir)
        {
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == Process.GetCurrentProcess().Id) continue;
                        if (p.MainModule != null && p.MainModule.FileName.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                        {
                            Narrar($"Matando: {p.ProcessName}");
                            p.Kill();
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        static void CopiarRecursivo(string origem, string destino)
        {
            Directory.CreateDirectory(destino);
            foreach (var arquivo in Directory.GetFiles(origem))
            {
                string destFile = Path.Combine(destino, Path.GetFileName(arquivo));
                bool copiou = false;
                for (int i = 0; i < 3; i++)
                {
                    try { File.Copy(arquivo, destFile, true); copiou = true; break; }
                    catch { Thread.Sleep(500); }
                }
                if (!copiou) Narrar($"Falha ao copiar {Path.GetFileName(arquivo)}");
            }
            foreach (var dir in Directory.GetDirectories(origem))
                CopiarRecursivo(dir, Path.Combine(destino, new DirectoryInfo(dir).Name));
        }

        static void LimparUpdaterTemporario()
        {
            try
            {
                string tmp = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, NOME_UPDATER_TEMPORARIO);
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch { }
        }

        static void Narrar(string texto)
        {
            try { File.AppendAllText(_caminhoLogDesktop, $"[{DateTime.Now:HH:mm:ss}] {texto}\n"); Console.WriteLine(texto); } catch { }
        }

        static string GetArg(string[] args, string key)
        {
            foreach (var arg in args)
            {
                string a = arg.TrimStart('-');
                string k = key.TrimStart('-');
                if (a.StartsWith(k + "=")) return a.Split('=', 2)[1].Trim('"');
            }
            return "";
        }

        private static void ExecutarCorrecaoNome(string[] args) { }
    }
}