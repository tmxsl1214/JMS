﻿using JMS.Dtos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Engines;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Way.Lib;

namespace JMS
{
    class ListenFileChangeChannel
    {
        AutoResetEvent _waitObj = new AutoResetEvent(false);
        List<string> _changedFiles = new List<string>();
        string[] _listeningFiles;
        IConfiguration _configuration;
        ILogger<ListenFileChangeChannel> _logger;
        string _root;
        public ListenFileChangeChannel(IConfiguration configuration, ILogger<ListenFileChangeChannel> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _root = configuration.GetValue<string>("ShareFolder");
            SystemEventCenter.ShareFileChanged += SystemEventCenter_ShareFileChanged;
        }

        private void SystemEventCenter_ShareFileChanged(object sender, string file)
        {
            if(_listeningFiles != null && _listeningFiles.Contains(file) )
            {
                lock (_changedFiles)
                {
                    _changedFiles.Add(file);
                }
                _waitObj.Set();
            }            
        }

        public void Handle(NetClient client,GatewayCommand cmd)
        {
            try
            {
                _listeningFiles = cmd.Content.FromJson<string[]>();
                _changedFiles.AddRange(_listeningFiles);

                while (true)
                {                    
                    if (_changedFiles.Count == 0)
                    {
                        client.WriteServiceData(new InvokeResult
                        {
                            Success = true
                        });
                        client.ReadServiceObject<InvokeResult>();
                    }
                    else
                    {
                        string[] sendFiles = null;
                        lock (_changedFiles)
                        {
                            sendFiles = _changedFiles.ToArray();
                            _changedFiles.Clear();
                        }

                        foreach(var file in sendFiles )
                        {
                            string fullpath = $"{_root}/{file}";
                            if (File.Exists(fullpath))
                            {
                                byte[] data = null;
                                try
                                {
                                    data = File.ReadAllBytes(fullpath);

                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogError(ex, ex.Message);
                                    continue;
                                }
                               
                                client.WriteServiceData(new InvokeResult
                                {
                                    Success = true,
                                    Data = file
                                });
                                
                                client.Write(data.Length);
                                client.Write(data);
                                client.ReadServiceObject<InvokeResult>();
                            }
                        }

                    }
                    

                    _waitObj.WaitOne(38000);
                }
            }
            catch (Exception ex)
            {                
                throw;
            }
            finally
            {
                SystemEventCenter.ShareFileChanged -= SystemEventCenter_ShareFileChanged;
            }
           
        }
    }
}
