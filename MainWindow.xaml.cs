using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Win32;
using MimeKit;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace stankin2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string _content = "";
        private string _publicPrivateKeys = "";
        private string _publicKey = "";

        public MainWindow() => InitializeComponent();

        #region Send

        private void ChooseFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Text files (*.txt)|*.txt";
            openFileDialog.InitialDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..\\..\\..\\contents"));
            if (openFileDialog.ShowDialog() == true)
            {
                _content = File.ReadAllText(openFileDialog.FileName);
                fileLoadedLabel.Visibility = Visibility.Visible;
                chooseFileButton.IsEnabled = false;
                chooseKeysButton.IsEnabled = true;
            }
        }

        private void ChooseKeysButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Text files (*.txt)|*.txt";
            openFileDialog.InitialDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..\\..\\..\\keys"));
            if (openFileDialog.ShowDialog() == true)
            {
                _publicPrivateKeys = File.ReadAllText(openFileDialog.FileName);

                if (_publicPrivateKeys.Contains("<Modulus>") &&
                    _publicPrivateKeys.Contains("<Exponent>") &&
                    _publicPrivateKeys.Contains("<P>") &&
                    _publicPrivateKeys.Contains("<Q>") &&
                    _publicPrivateKeys.Contains("<DP>") &&
                    _publicPrivateKeys.Contains("<DQ>") &&
                    _publicPrivateKeys.Contains("<InverseQ>") &&
                    _publicPrivateKeys.Contains("<D>"))
                {
                    keysLoadedLabel.Visibility = Visibility.Visible;
                    chooseKeysButton.IsEnabled = false;
                    themeTextBox.IsEnabled = true;
                }
                else
                {
                    string messageBoxText = "Not valid file. Need public + private keys.";
                    string caption = "Error";
                    MessageBoxButton button = MessageBoxButton.OK;
                    MessageBoxImage icon = MessageBoxImage.Error;
                    MessageBox.Show(messageBoxText, caption, button, icon, MessageBoxResult.Yes);
                }
            }
        }

        private void ThemeTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            sendButton.IsEnabled = !string.IsNullOrWhiteSpace(themeTextBox.Text);
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(_content));

            using var rsaServer = new RSACryptoServiceProvider();
            rsaServer.FromXmlString(_publicPrivateKeys);
            var encryptedHash = rsaServer.SignData(hash, CryptoConfig.MapNameToOID("SHA512"));

            using var smtp = new SmtpClient("smtp.mail.ru", 587);
            smtp.UseDefaultCredentials = false;
            smtp.EnableSsl = true;
            smtp.Credentials = new NetworkCredential()
            {
                UserName = Properties.Settings.Default.User1,
                Password = Properties.Settings.Default.Pass1
            };

            using var contentStream = GenerateStreamFromString(_content);
            using var encryptedHashStream = GenerateStreamFromBytes(encryptedHash);

            var msg = new MailMessage()
            {
                Subject = themeTextBox.Text,
                From = new MailAddress(Properties.Settings.Default.User1)
            };

            msg.Attachments.Add(new Attachment(contentStream, "Content.txt"));
            msg.Attachments.Add(new Attachment(encryptedHashStream, "EncryptedHash.txt"));

            msg.To.Add(new MailAddress(Properties.Settings.Default.User2));

            smtp.Send(msg);
        }

        #endregion

        #region Recieve

        private void ChooseKeyButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Text files (*.txt)|*.txt";
            openFileDialog.InitialDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..\\..\\..\\keys"));
            if (openFileDialog.ShowDialog() == true)
            {
                _publicKey = File.ReadAllText(openFileDialog.FileName);

                if (_publicKey.Contains("<Modulus>") &&
                    _publicKey.Contains("<Exponent>") &&
                    !_publicKey.Contains("<P>") &&
                    !_publicKey.Contains("<Q>") &&
                    !_publicKey.Contains("<DP>") &&
                    !_publicKey.Contains("<DQ>") &&
                    !_publicKey.Contains("<InverseQ>") &&
                    !_publicKey.Contains("<D>"))
                {
                    keyLoadedLabel.Visibility = Visibility.Visible;
                    chooseKeyButton.IsEnabled = false;
                    themeRecieveTextBox.IsEnabled = true;
                }
                else
                {
                    string messageBoxText = "Not valid file. Need only public key.";
                    string caption = "Error";
                    MessageBoxButton button = MessageBoxButton.OK;
                    MessageBoxImage icon = MessageBoxImage.Error;
                    MessageBox.Show(messageBoxText, caption, button, icon, MessageBoxResult.Yes);
                }
            }
        }

        private void ThemeRecieveTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            getAndCheckButton.IsEnabled = !string.IsNullOrWhiteSpace(themeRecieveTextBox.Text);
        }

        private void GetAndCheckButton_Click(object sender, RoutedEventArgs e)
        {
            using var md5 = MD5.Create();
            using var imap = new ImapClient();

            imap.Connect("imap.mail.ru", 993, true);
            imap.Authenticate(Properties.Settings.Default.User2, Properties.Settings.Default.Pass2);
            imap.Inbox.Open(FolderAccess.ReadOnly);
            var ids = imap.Inbox.Search(SearchQuery.SentSince(DateTime.Now.AddYears(-1)));
            var messages = imap.Inbox.Fetch(ids, MessageSummaryItems.Envelope | MessageSummaryItems.BodyStructure);

            if (messages is not null && messages.Count > 0)
            {
                var message = messages.Where(m => m.NormalizedSubject == themeRecieveTextBox.Text).OrderByDescending(m => m.Date).FirstOrDefault();

                if (message is not null)
                {
                    if (message.Attachments.Count() == 2)
                    {
                        var contentAtt = message.Attachments.FirstOrDefault(m => m.FileName == "Content.txt");
                        var contentPart = (MimePart)imap.Inbox.GetBodyPart(message.UniqueId, contentAtt);

                        var encryptedHashAtt = message.Attachments.FirstOrDefault(m => m.FileName == "EncryptedHash.txt");
                        var encryptedHashPart = (MimePart)imap.Inbox.GetBodyPart(message.UniqueId, encryptedHashAtt);

                        if (contentPart is not null && encryptedHashPart is not null)
                        {
                            var pathDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..\\..\\..\\temp"));

                            if (!Directory.Exists(pathDir))
                                Directory.CreateDirectory(pathDir);

                            var contentPath = Path.Combine(pathDir, contentPart.FileName);
                            var encryptedHashPath = Path.Combine(pathDir, encryptedHashPart.FileName);

                            using (var stream = File.Create(contentPath))
                            {
                                contentPart.Content.DecodeTo(stream);
                            }

                            using (var stream = File.Create(encryptedHashPath))
                            {
                                encryptedHashPart.Content.DecodeTo(stream);
                            }

                            var content = File.ReadAllText(contentPath);
                            var encryptedHash = File.ReadAllBytes(encryptedHashPath);

                            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(content));

                            using var rsaServer = new RSACryptoServiceProvider();
                            rsaServer.FromXmlString(_publicKey);
                            var success = rsaServer.VerifyData(hash, CryptoConfig.MapNameToOID("SHA512"), encryptedHash);

                            if (success)
                            {
                                validationLabel.Foreground = Brushes.Green;
                                validationLabel.Content = "VALID";
                            }
                            else
                            {
                                validationLabel.Foreground = Brushes.Red;
                                validationLabel.Content = "NOT VALID";
                            }

                            File.Delete(contentPath);
                            File.Delete(encryptedHashPath);
                        }
                        else
                        {
                            string messageBoxText = "Cant parse attachments";
                            string caption = "Error";
                            MessageBoxButton button = MessageBoxButton.OK;
                            MessageBoxImage icon = MessageBoxImage.Error;
                            MessageBox.Show(messageBoxText, caption, button, icon, MessageBoxResult.Yes);
                        }
                    }
                    else
                    {
                        string messageBoxText = "Attachments not found";
                        string caption = "Error";
                        MessageBoxButton button = MessageBoxButton.OK;
                        MessageBoxImage icon = MessageBoxImage.Error;
                        MessageBox.Show(messageBoxText, caption, button, icon, MessageBoxResult.Yes);
                    }
                }
                else
                {
                    string messageBoxText = "Messages not found";
                    string caption = "Error";
                    MessageBoxButton button = MessageBoxButton.OK;
                    MessageBoxImage icon = MessageBoxImage.Error;
                    MessageBox.Show(messageBoxText, caption, button, icon, MessageBoxResult.Yes);
                }
            }
            else
            {
                string messageBoxText = "Messages not found";
                string caption = "Error";
                MessageBoxButton button = MessageBoxButton.OK;
                MessageBoxImage icon = MessageBoxImage.Error;
                MessageBox.Show(messageBoxText, caption, button, icon, MessageBoxResult.Yes);
            }
        }

        #endregion

        #region Helpers

        private static Stream GenerateStreamFromString(string s)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        private static Stream GenerateStreamFromBytes(byte[] b)
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write(b);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }


        #endregion
    }
}
