﻿using BiliRoku.Commentlib;
using System;
using System.Threading.Tasks;

namespace BiliRoku.Bililivelib
{
    internal class Downloader
    {
        private readonly MainWindow _mw;
        private string _roomid;
        private string _flvUrl;
        private bool _flvRunning;
        private CommentProvider _commentProvider;
        private FlvDownloader _flvDownloader;
        private long _recordedSize;
        private RoomInfo _roomInfo;

        private bool _downloadCommentOption = true;
        private bool _autoStart = true;

        public bool IsRunning { get; private set; }
        public RoomInfo RoomInfo { get => _roomInfo; set => _roomInfo = value; }

        public Downloader(MainWindow mw)
        {
            _mw = mw;
            IsRunning = false;
            _flvRunning = false;
        }

        public async void Start()
        {
            try
            {
                _mw.SetProcessingBtn();
                if (IsRunning)
                {
                    _mw.AppendLogln("ERROR,", _roomInfo.Remark + ":已经是运行状态了。");
                    return;
                }
                //设置运行状态。
                IsRunning = true;
                RoomInfo.IsRun = true;

                //读取设置
                var originalRoomId = RoomInfo.RoomId;
                var savepath = RoomInfo.SaveLocation;
                _downloadCommentOption = Convert.ToBoolean(RoomInfo.IsDownloadCmt);
                _autoStart = true;

                //准备查找下载地址
                var pathFinder = new PathFinder(_mw);
                //查找真实房间号
                _roomid = await pathFinder.GetRoomid(originalRoomId);
                if (_roomid != null)
                {
                    _mw.SetStartBtn();
                }
                else
                {
                    _mw.AppendLogln("ERROR,", _roomInfo.Remark + ":未取得真实房间号");
                    Stop();
                    return; //停止并退出
                }
                //查找真实下载地址
                try
                {
                    _flvUrl = await pathFinder.GetTrueUrl(_roomid);
                }
                catch
                {
                    _mw.AppendLogln("ERROR,", _roomInfo.Remark + ":未取得下载地址");
                    Stop();
                    return; //停止并退出
                }

                var cmtProvider = ReceiveComment();
                _flvDownloader = new FlvDownloader(_roomid, savepath, RoomInfo.Remark, _downloadCommentOption, cmtProvider); _flvDownloader.Info += _flvDownloader_Info;
                CheckStreaming();
                try
                {
                    _flvDownloader.Start(_flvUrl);
                }
                catch (Exception e)
                {
                    _mw.AppendLogln("ERROR,", _roomInfo.Remark + ":下载视频流时出错：" + e.Message);
                    Stop();
                }
            }catch(Exception e)
            {
                _mw.AppendLogln("ERROR,", _roomInfo.Remark + ":未知错误：" + e.Message);
                Stop();
            }
        }

        private void _flvDownloader_Info(object sender, DownloadInfoArgs e)
        {
            _recordedSize = e.Bytes;
        }

        private async void CheckStreaming()
        {
            await Task.Delay(2000);
            try
            {
                if (_flvDownloader == null)
                {
                    return;
                }
                if (_recordedSize <= 1)
                {
                    if (_flvDownloader.IsDownloading)
                    {
                        _flvDownloader.Stop();
                    }
                    _mw.Dispatcher.Invoke(() =>
                    {
                        RoomInfo.Status = "未直播";
                        _mw.refreshData();
                    });
                }
                else
                {
                    _mw.Dispatcher.Invoke(() =>
                    {
                        RoomInfo.Status = "正在直播";
                        _mw.refreshData();
                    });
                }
            }catch(Exception ex)
            {
                _mw.AppendLogln("ERROR:", _roomInfo.Remark + ":在检查直播状态时发生未知错误：" + ex.Message);
                Stop();
            }
        }

        private static string FormatSize(long size)
        {
            if (size <= 1024)
            {
                return size.ToString("F2") + "B";
            }
            if (size <= 1048576)
            {
                return (size / 1024.0).ToString("F2") + "KB";
            }
            if (size <= 1073741824)
            {
                return (size / 1048576.0).ToString("F2") + "MB";
            }
            if (size <= 1099511627776)
            {
                return (size / 1073741824.0).ToString("F2") + "GB";
            }
            return (size / 1099511627776.0).ToString("F2") + "TB";
        }

        public void Stop()
        {
            if (IsRunning)
            {
                IsRunning = false;
                _recordedSize = 0;
                if (_flvDownloader != null)
                {
                    _flvDownloader.Stop();
                    _flvDownloader = null;
                }
                _commentProvider?.Disconnect();
                _mw.AppendLogln("INFO,", _roomInfo.Remark + ":停止");
                RoomInfo.IsRun = false;
                RoomInfo.Status = "";
                _mw.refreshData();
            }
            else
            {
                _mw.AppendLogln("ERROR,", _roomInfo.Remark + ":已经是停止状态了");
                RoomInfo.IsRun = false;
                RoomInfo.Status = "";
                _mw.refreshData();
            }
            _mw.SetStopBtn();
        }

        private CommentProvider ReceiveComment()
        {
            try
            {
                _commentProvider = new CommentProvider(_roomid, _mw);
                _commentProvider.OnDisconnected += CommentProvider_OnDisconnected;
                _commentProvider.OnReceivedRoomCount += CommentProvider_OnReceivedRoomCount;
                _commentProvider.OnReceivedComment += CommentProvider_OnReceivedComment;
                _commentProvider.Connect();
                return _commentProvider;
            }catch(Exception e)
            {
                _mw.AppendLogln("ERROR,", _roomInfo.Remark + ":弹幕服务器出错：" + e.Message);
                return null;
            }
        }

        private async void CommentProvider_OnReceivedComment(object sender, ReceivedCommentArgs e)
        {
            try
            {
                //接收到弹幕时的处理。
                if (e.Comment.MsgType != MsgTypeEnum.LiveStart)
                {
                    if (e.Comment.MsgType != MsgTypeEnum.LiveEnd) return;
                    _mw.AppendLogln("INFO,", _roomInfo.Remark + ":[主播结束直播]");
                    RoomInfo.IsRun = false;
                    RoomInfo.Status = "";
                    _flvDownloader?.Stop(); if (!_autoStart)
                    {
                        Stop();
                    }
                    else
                    {
                        _mw.Dispatcher.Invoke(() =>
                        {
                            RoomInfo.Status = "未直播";
                            _mw.refreshData();
                        });
                    }
                }
                else
                {
                    _mw.AppendLogln("INFO", "[主播开始直播]");

                    if (!_autoStart || _flvDownloader.IsDownloading) return;
                    //准备查找下载地址
                    var pathFinder = new PathFinder(_mw);

                    //查找真实下载地址
                    try
                    {
                        if (_flvRunning) return;
                        _flvRunning = true;
                        _flvUrl = await pathFinder.GetTrueUrl(_roomid);
                        _flvRunning = false;
                    }
                    catch
                    {
                        _mw.AppendLogln("ERROR,", _roomInfo.Remark + ":未取得下载地址");
                        Stop();
                        return; //停止并退出
                    }

                    _mw.AppendLogln("INFO,", _roomInfo.Remark + ":下载地址已更新。");

                    try
                    {
                        _flvDownloader.Start(_flvUrl);
                    }
                    catch (Exception exception)
                    {
                        _mw.AppendLogln("ERROR,", _roomInfo.Remark + ":下载视频流时出错：" + exception.Message);
                        Stop();
                    }

                    CheckStreaming();
                }
            }catch(Exception ex)
            {
                _mw.AppendLogln("ERROR,", _roomInfo.Remark + ":在收取弹幕时发生未知错误：" + ex.Message);
                Stop();
            }
        }

        private void CommentProvider_OnReceivedRoomCount(object sender, ReceivedRoomCountArgs e)
        {
        }

        private void CommentProvider_OnDisconnected(object sender, DisconnectEvtArgs e)
        {
            _mw.AppendLogln("INFO,", _roomInfo.Remark + ":弹幕服务器断开");

            //如果不是用户触发的，则尝试重连。
            if (!IsRunning) return;
            _mw.AppendLogln("INFO,", _roomInfo.Remark + ":尝试重新连接弹幕服务器");
            _commentProvider.Connect();
        }
    }
}
