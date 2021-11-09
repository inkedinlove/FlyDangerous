﻿using System;
using System.Threading.Tasks;
using Audio;
using Core;
using Menus.Main_Menu.Components;
using UnityEngine;
using UnityEngine.UI;

namespace Menus.Main_Menu {
    public class ServerBrowserMenu : MenuBase {
        [SerializeField] private LobbyMenu lobbyMenu;
        [SerializeField] private ConnectingDialog connectingDialog;

        [SerializeField] private GameObject refreshingIndicator;
        [SerializeField] private Transform serverEntryContainer;
        [SerializeField] private ServerBrowserEntry serverBrowserEntryPrefab;

        public LobbyMenu LobbyMenu => lobbyMenu;
        public ConnectingDialog ConnectingDialog => connectingDialog;
        
        protected override void OnOpen() {
            RefreshList();
        }

        public async void RefreshList() {
            PlayApplySound();
            if (FdNetworkManager.Instance.OnlineService != null) {
                try {
                    var existingEntries = serverEntryContainer.gameObject.GetComponentsInChildren<ServerBrowserEntry>();
                    foreach (var serverBrowserEntry in existingEntries) {
                        Destroy(serverBrowserEntry.gameObject);
                    }

                    refreshingIndicator.SetActive(true);
                    var servers = await FdNetworkManager.Instance.OnlineService.GetLobbyList();
                    refreshingIndicator.SetActive(false);

                    foreach (var serverId in servers) {
                        var serverEntry = Instantiate(serverBrowserEntryPrefab, serverEntryContainer);
                        serverEntry.LobbyId = serverId;
                    }

                    foreach (var serverEntry in serverEntryContainer.GetComponentsInChildren<ServerBrowserEntry>()) {
                        if (serverEntry != null) {
                            await serverEntry.Refresh();
                        }
                    }
                } 
                // Discard cancellation exceptions - retrying before completion will cancel pending operations.
                // This is intended.
                catch ( OperationCanceledException ) {} 
            }
        }

        public void OpenHostPanel() {
            Game.Instance.SessionStatus = SessionStatus.LobbyMenu;
            Progress(lobbyMenu);
            lobbyMenu.StartHost();
        }

        public void ClosePanel() {
            Cancel();
        }
    }
}