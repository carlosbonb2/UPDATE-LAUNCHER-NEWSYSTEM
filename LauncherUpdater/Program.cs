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
        static string _exeNameSolicitado = "PDV_Launcher.exe";
        static string _caminhoZipDoLauncher = string.Empty;

        static void Main(string[] args)
        {
            try
            {
                // 1. REGRA DE OURO: Mudar o diretório de trabalho para TEMP imediatamente.
                // Isso impede que o Updater trave a pasta que ele quer apagar.
                Directory.SetCurrentDirectory(Path.GetTempPath());

                LimparUpdaterTemporario();

                File.WriteAllText(_caminhoLogDesktop, $"--- INICIO DA ATUALIZAÇÃO: {DateTime.Now} ---\n");
                Narrar("Updater iniciado. Diretório de trabalho ajustado para TEMP.");
                Narrar($"Args: {string.Join(" ", args)}");

                if (args.Length == 0) return;

                if (args.Contains("--fix-mode")) return; // Ignorado

                ExecutarAtualizacaoPadrao(args);
            }
            catch (Exception ex)
            {
                Narrar("\n------------------------------------------------");
                Narrar("!!! ERRO CRÍTICO !!!");
                Narrar(ex.ToString());
                Narrar("------------------------------------------------");
                // Tenta manter janela aberta se der erro fatal
                Console.WriteLine("ERRO FATAL. Pressione ENTER...");
                try { Console.ReadLine(); } catch { }
            }
            finally
            {
                Narrar($"\nFim: {DateTime.Now}.");
                try { if (File.Exists(_caminhoZipDoLauncher)) File.Delete(_caminhoZipDoLauncher); } catch { }
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

            string backupDir = targetDir + "_BACKUP_" + DateTime.Now.ToString("HHmmss");

            Narrar($"PID Pai: {parentPid} | Alvo: {targetDir}");

            // 1. Matar Processos e Esperar (Modo Agressivo)
            GarantirMorteDoProcesso(parentPid);
            MatarProcessosNaPasta(targetDir);

            Narrar("Aguardando estabilização do sistema (2s)...");
            Thread.Sleep(2000);

            // 2. BACKUP E PREPARAÇÃO (Com Fail-Safe)
            // Se não der para mover a pasta (acesso negado), nós PULAMOS o backup e sobrescrevemos.
            // É melhor atualizar sem backup do que falhar e não atualizar nada.
            bool backupCriado = false;

            try
            {
                if (Directory.Exists(targetDir))
                {
                    Narrar($"Tentando mover instalação atual para backup: {backupDir}");

                    // Tenta limpar backup antigo se existir
                    if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);

                    // TENTA MOVER (A operação perigosa)
                    Directory.Move(targetDir, backupDir);

                    backupCriado = true;
                    Directory.CreateDirectory(targetDir); // Cria pasta limpa
                    Narrar("Backup criado com sucesso (Pasta movida).");
                }
                else
                {
                    Directory.CreateDirectory(targetDir);
                }
            }
            catch (Exception ex)
            {
                Narrar($"AVISO: Não foi possível criar backup (Arquivo travado?). {ex.Message}");
                Narrar("MODO DE SOBRESCRITA ATIVADO: Vou instalar por cima dos arquivos existentes.");
                backupCriado = false;
                // Não damos throw aqui. Seguimos em frente.
            }

            // 3. INSTALAÇÃO
            try
            {
                // A. Instala arquivos novos do ZIP
                AplicarAtualizacao(zipPath, targetDir, _exeNameSolicitado);

                // B. Restaura dados do usuário (apenas se o backup existiu)
                if (backupCriado)
                {
                    Narrar("Restaurando dados do usuário...");
                    RestaurarDadosUsuario(backupDir, targetDir);
                }
                else
                {
                    Narrar("Backup não foi criado, mantendo arquivos locais originais (Sobrescrita).");
                }

                // C. Validação e Reinício
                string caminhoExeFinal = Path.Combine(targetDir, _exeNameSolicitado);
                if (File.Exists(caminhoExeFinal))
                {
                    Narrar("Limpando backup...");
                    if (backupCriado && Directory.Exists(backupDir))
                    {
                        try { Directory.Delete(backupDir, true); } catch { Narrar("Aviso: Não consegui apagar a pasta de backup final."); }
                    }

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
                Narrar($"FALHA NA INSTALAÇÃO ({ex.Message}).");

                // Tenta Rollback se possível
                if (backupCriado)
                {
                    Narrar("Tentando ROLLBACK...");
                    try
                    {
                        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                        if (Directory.Exists(backupDir)) Directory.Move(backupDir, targetDir);
                        Narrar("Rollback concluído.");
                    }
                    catch (Exception rbEx) { Narrar($"Falha no Rollback! {rbEx.Message}"); }
                }
                throw;
            }
        }

        private static void GarantirMorteDoProcesso(int pid)
        {
            if (pid <= 0) return;
            try
            {
                Process p = Process.GetProcessById(pid);
                Narrar($"Aguardando processo pai {pid} encerrar...");
                p.WaitForExit(3000); // Espera 3s
                if (!p.HasExited)
                {
                    Narrar($"Pai {pid} teimoso. Forçando encerramento...");
                    p.Kill();
                    p.WaitForExit(1000);
                }
            }
            catch { /* Já morreu */ }
        }

        private static void RestaurarDadosUsuario(string pastaBackup, string pastaDestino)
        {
            try
            {
                // NÃO restauramos local_version.txt para permitir atualização de versão
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

                string pastaBackupsAntigos = Path.Combine(pastaBackup, "Backups");
                string pastaBackupsNova = Path.Combine(pastaDestino, "Backups");
                if (Directory.Exists(pastaBackupsAntigos))
                {
                    if (!Directory.Exists(pastaBackupsNova)) Directory.Move(pastaBackupsAntigos, pastaBackupsNova);
                }
            }
            catch (Exception ex) { Narrar($"AVISO: Erro na restauração de dados: {ex.Message}"); }
        }

        private static void AplicarAtualizacao(string zipPath, string targetDir, string exeNameFinal)
        {
            string tempFolder = Path.Combine(Path.GetTempPath(), "PDV_Ext_" + Guid.NewGuid().ToString().Substring(0, 5));
            Narrar($"Extraindo ZIP...");

            try
            {
                Directory.CreateDirectory(tempFolder);
                ZipFile.ExtractToDirectory(zipPath, tempFolder, true);

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
                            Narrar($"Matando processo travando pasta: {p.ProcessName}");
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
                    catch { Thread.Sleep(500); } // Retry simples
                }
                if (!copiou) Narrar($"Aviso: Falha ao copiar {Path.GetFileName(arquivo)} (Arquivo em uso?)");
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