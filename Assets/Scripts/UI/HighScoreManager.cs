using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Solana.Unity.Rpc.Models;
using Solana.Unity.SDK;
using Solana.Unity.Soar.Program;
using Solana.Unity.Programs;
using Solana.Unity.Soar;
using Solana.Unity.Wallet;

public struct Score
{
    public string username;
    public ulong score;
}

public class HighScoreManager : SingletonMonobehaviour<HighScoreManager>
{
    public List<Score> scores = new();
    protected override void Awake()
    {
        base.Awake();
        DontDestroyOnLoad(gameObject);
    }

    public void Refresh()
    {
        GetScores((success) => 
        {
            if (success && scores.Count > 0 && SceneManager.GetActiveScene().name == "MainMenuScene")
            {
                FindObjectOfType<DisplayHighScoresUI>(true).DisplayScores(scores);
            }
        });
    }

    /// <summary>
    /// Add score to high scores list
    /// </summary>
    public async void AddScore(ulong playerScore)
    {
        var tx = new Transaction()
        {
            FeePayer = Web3.Account,
            Instructions = new List<TransactionInstruction>(),
            RecentBlockHash = await Web3.BlockHash()
        };

        var game = WalletManager.instance.soarGame;
        var leaderboard = WalletManager.instance.soarLeaderboard;
        var playerAccount = SoarPda.PlayerPda(Web3.Account);
        var playerScores = SoarPda.PlayerScoresPda(playerAccount, leaderboard);

        if (!await WalletManager.instance.IsPdaInitialized(playerScores))
        {
            var registerPlayerAccounts = new RegisterPlayerAccounts()
            {
                Payer = Web3.Account,
                User = Web3.Account,
                PlayerAccount = playerAccount,
                Game = game,
                Leaderboard = leaderboard,
                NewList = playerScores,
                SystemProgram = SystemProgram.ProgramIdKey
            };
            var registerPlayerIx = SoarProgram.RegisterPlayer(
                registerPlayerAccounts,
                SoarProgram.ProgramIdKey
            );
            tx.Add(registerPlayerIx);
        }

        Account authWallet = new("4aecpyrADpd3LNaZqmxHbaB7FEDQi89AKPqHSY6k3Kk8JSYAtZQ26rwL77c4JpMdRpESAv9KSRQrLQUG9XdCmq2i", "Ggg31TYu5hzH8x7x47W4ZXLqnEPSSqV1nF4YbJAfJyNi");
        var addLeaderboardAccounts = new SubmitScoreAccounts()
        {
            Authority = authWallet.PublicKey,
            Payer = Web3.Account,
            PlayerAccount = playerAccount,
            Game = game,
            Leaderboard = leaderboard,
            PlayerScores = playerScores,
            TopEntries = SoarPda.LeaderboardTopEntriesPda(leaderboard),
            SystemProgram = SystemProgram.ProgramIdKey
        };

        var submitScoreIx = SoarProgram.SubmitScore(
            accounts: addLeaderboardAccounts,
            score: playerScore,
            SoarProgram.ProgramIdKey
        );

        tx.Add(submitScoreIx);

        tx.PartialSign(Web3.Account);
        tx.PartialSign(authWallet);

        var res = await Web3.Wallet.SignAndSendTransaction(tx);
        Debug.Log($"Result: {Newtonsoft.Json.JsonConvert.SerializeObject(res)}");
    }

    private async void GetScores(Action<bool> result)
    {
        scores = new();
        var client = new SoarClient(Web3.Rpc, Web3.WsRpc);
        var topEntries = await client.GetLeaderTopEntriesAsync(WalletManager.instance.soarTopEntries);
        for (int i = 0; i < topEntries.ParsedResult.TopScores.Length; i++)
        {
            if (topEntries.ParsedResult.TopScores[i].Player != "11111111111111111111111111111111")
            {
                Debug.Log(topEntries.ParsedResult.TopScores[i].Player);
                string username = (await client.GetPlayerAsync(topEntries.ParsedResult.TopScores[i].Player)).ParsedResult.Username;
                scores.Add(new()
                {
                    username = username,
                    score = topEntries.ParsedResult.TopScores[i].Entry.Score
                });
            }
        }

        result(true);
    }
}