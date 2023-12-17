using UnityEngine;
using System.Threading.Tasks;
using Solana.Unity.Wallet;
using Solana.Unity.SDK;
using Solana.Unity.Rpc;
using Solana.Unity.Rpc.Models;
using System.Collections.Generic;
using System;
using Solana.Unity.Rpc.Types;
using Solana.Unity.Soar.Program;
using SolDungeons.Accounts;
using System.Text;
using Solana.Unity.Programs;
using SolDungeons.Program;
using Solana.Unity.Rpc.Core.Http;
using Cysharp.Threading.Tasks;
using System.Buffers.Binary;
using SolDungeons;

[Serializable]
public struct NFTDatas
{
    public string publicKey;
    public string name;
    public string description;
    public int price;
    public Sprite sprite;
}

public class WalletManager : MonoBehaviour
{
    public static WalletManager instance { get; private set; }
    public string sessionPassword;
    public NFTMetadatas metadatas;
    [HideInInspector] public float DunTokenBalance;
    [HideInInspector] public float playerLevel;
    [HideInInspector] public OwnedNFTIds ownedNFTIds;

    [HideInInspector] public PublicKey programId = new("CW7thTzLfzZop6TtHrD4FgjcJzxNMiscHRR9XrdW4T14");
    [HideInInspector] public PublicKey dunMint = new("DxrjnijMsSbcZExcH8bpkEAicHHPdW8G6iZ4op4vWEqH");
    [HideInInspector] public PublicKey userNftMeta = new("3qySq5hG9yBUpcMez4zDaeYfC7yiCTsu71wx6i2R9xQH");
    [HideInInspector] public PublicKey soarGame = new("2HBaVLdm8dKyA9NGVGoVJkRS6jbtYX2f1vh2PzTdK5XL");
    [HideInInspector] public PublicKey soarLeaderboard = new("7d8YfBR3oPNncgwPCkGFnyapcLeUHfzBYQPDQe3U1A95");
    [HideInInspector] public PublicKey soarTopEntries = new("DAoMVfQ1NgJWvJKXpkhvqG6LzKS2nrcj1HSQyRbsium3");
    [HideInInspector] public PublicKey playerPDA = null;
    [HideInInspector] public PublicKey vaultPDA = null;
    [HideInInspector] public PublicKey playerSoarPDA = null;
    [HideInInspector] public User user;
    [HideInInspector] public IRpcClient rpcClient;
    [HideInInspector] public SessionWallet sessionWallet;

    void Awake()
    {
        if (instance != null && instance != this)
            Destroy(gameObject);
        else
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    public async Task GetPDAs()
    {
        playerSoarPDA = SoarPda.PlayerPda(Web3.Account.PublicKey);

        PublicKey.TryFindProgramAddress(new[]{
            Encoding.UTF8.GetBytes("PLAYER"),
            Web3.Account.PublicKey
            }, programId, out playerPDA, out var _);

        PublicKey.TryFindProgramAddress(new[]{
            Encoding.UTF8.GetBytes("VAULT")
            }, programId, out vaultPDA, out var _);

        Debug.Log($"Vault PDA: {vaultPDA}");
        Debug.Log($"Public Key: {Web3.Account.PublicKey}");

        await SetupSessionWallet();
    }

    private async Task SetupSessionWallet()
    {
        sessionWallet = await SessionWallet.GetSessionWallet(programId, sessionPassword);

        rpcClient = ClientFactory.GetClient(Cluster.DevNet);
        if (!await sessionWallet.IsSessionTokenInitialized())
        {
            var txSession = new Transaction()
            {
                FeePayer = Web3.Account,
                Instructions = new List<TransactionInstruction>(),
                RecentBlockHash = (await rpcClient.GetLatestBlockHashAsync()).Result.Value.Blockhash
            };

            txSession.Instructions.Add(sessionWallet.CreateSessionIX(true, DateTimeOffset.UtcNow.AddHours(23).ToUnixTimeSeconds()));
            txSession.PartialSign(new[] { Web3.Account, sessionWallet.Account });

            var reqRes = await Web3.Wallet.SignAndSendTransaction(txSession, commitment: Commitment.Finalized);
            Debug.Log($"Result: {Newtonsoft.Json.JsonConvert.SerializeObject(reqRes)}");
        }
    }

    public async Task AuthenticateWithWallet()
    {
#if UNITY_EDITOR
        await Web3.Instance.LoginWeb3Auth(Provider.GOOGLE);
#else
        await Web3.Instance.LoginWalletAdapter();
#endif
    }

    public async UniTask<bool> IsPdaInitialized(PublicKey pda)
    {
        var accountInfoAsync = await Web3.Rpc.GetAccountInfoAsync(pda);
        return accountInfoAsync.WasSuccessful && accountInfoAsync.Result?.Value != null;
    }

    public async Task RecieveToken(string mint, int amount)
    {
        var ATAtx = new Transaction()
        {
            FeePayer = Web3.Account,
            Instructions = new List<TransactionInstruction>(),
            RecentBlockHash = (await rpcClient.GetLatestBlockHashAsync()).Result.Value.Blockhash
        };

        var mintRes = await Web3.Rpc.GetAccountInfoAsync(AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(Web3.Account.PublicKey, new PublicKey(mint)));
        if (!mintRes.WasSuccessful || mintRes.Result?.Value == null)
        {
            TransactionInstruction createATAIx = AssociatedTokenAccountProgram.CreateAssociatedTokenAccount(Web3.Account.PublicKey, Web3.Account.PublicKey, new PublicKey(mint));
            ATAtx.Add(createATAIx);

            RequestResult<string> ATAres = await Web3.Wallet.SignAndSendTransaction(ATAtx, commitment: Commitment.Finalized);
            Debug.Log($"Result: {Newtonsoft.Json.JsonConvert.SerializeObject(ATAres)}");
        }

        var tx = new Transaction()
        {
            FeePayer = sessionWallet.Account,
            Instructions = new List<TransactionInstruction>(),
            RecentBlockHash = (await rpcClient.GetLatestBlockHashAsync()).Result.Value.Blockhash
        };

        PublicKey VaultATA = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(vaultPDA, new PublicKey(mint));
        PublicKey PlayerATA = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(Web3.Account.PublicKey, new PublicKey(mint));

        AddTokenAccounts accounts = new()
        {
            User = playerPDA,
            VaultPda = vaultPDA,
            VaultAta = VaultATA,
            UserAta = PlayerATA,
            GameToken = new PublicKey(mint),
            TokenProgram = TokenProgram.ProgramIdKey,
            AssociatedTokenProgram = AssociatedTokenAccountProgram.ProgramIdKey,
            Signer = sessionWallet.Account.PublicKey,
            SessionToken = sessionWallet.SessionTokenPDA,
            SystemProgram = SystemProgram.ProgramIdKey
        };

        tx.Add(SolDungeonsProgram.AddToken(accounts, (ulong)amount, programId));

        RequestResult<string> res = await sessionWallet.SignAndSendTransaction(tx);
        Debug.Log($"Result: {Newtonsoft.Json.JsonConvert.SerializeObject(res)}");
    }

    public async Task SendToken(string mint, int amount)
    {
        var tx = new Transaction()
        {
            FeePayer = sessionWallet.Account,
            Instructions = new List<TransactionInstruction>(),
            RecentBlockHash = (await rpcClient.GetLatestBlockHashAsync()).Result.Value.Blockhash
        };

        PublicKey VaultATA = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(vaultPDA, new PublicKey(mint));
        PublicKey PlayerATA = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(Web3.Account.PublicKey, new PublicKey(mint));
        ReduceTokenAccounts accounts = new()
        {
            User = playerPDA,
            Signer = sessionWallet.Account.PublicKey,
            SessionToken = sessionWallet.SessionTokenPDA,
            SystemProgram = SystemProgram.ProgramIdKey,
            VaultPda = vaultPDA,
            VaultAta = VaultATA,
            UserAta = PlayerATA,
            GameToken = new PublicKey(mint),
            TokenProgram = TokenProgram.ProgramIdKey,
            AssociatedTokenProgram = AssociatedTokenAccountProgram.ProgramIdKey,
            SignerWallet = Web3.Account.PublicKey
        };

        tx.Add(SolDungeonsProgram.ReduceToken(accounts, (ulong)amount, programId));

        List<Account> signers = new() { sessionWallet.Account, Web3.Account };
        tx.Sign(signers);

        RequestResult<string> res = await sessionWallet.SignAndSendTransaction(tx);
        Debug.Log($"Result: {Newtonsoft.Json.JsonConvert.SerializeObject(res)}");
    }

    public async Task AssignPlayerCharacter(PublicKey characterMint)
    {
        Transaction assignPlayerTx = new()
        {
            FeePayer = sessionWallet.Account,
            Instructions = new List<TransactionInstruction>(),
            RecentBlockHash = (await rpcClient.GetLatestBlockHashAsync()).Result.Value.Blockhash
        };

        int id = 0;
        for (int i = 0; i < metadatas.charactersMetadata.Length; i++)
        {
            if (metadatas.charactersMetadata[i].publicKey == characterMint)
            {
                id = i;
                break;
            }
        }

        PublicKey.TryFindProgramAddress(new[]{
            Encoding.UTF8.GetBytes(id.ToString()),
            Web3.Account.PublicKey.KeyBytes
        }, programId, out PublicKey playerCharacterPDA, out var _);

        var accountsAssignCharacter = new AssignPlayerCharacterAccounts()
        {
            Signer = sessionWallet.Account.PublicKey,
            User = playerPDA,
            UserCharacter = playerCharacterPDA,
            SessionToken = sessionWallet.SessionTokenPDA,
            SystemProgram = SystemProgram.ProgramIdKey
        };

        var playerCharacterIx = SolDungeonsProgram.AssignPlayerCharacter(
            accountsAssignCharacter,
            (byte)id,
            programId
        );

        assignPlayerTx.Add(playerCharacterIx);

        RequestResult<string> resTx = await sessionWallet.SignAndSendTransaction(assignPlayerTx);
        Debug.Log($"Result: {Newtonsoft.Json.JsonConvert.SerializeObject(resTx)}");
    }

    public async Task LockCurrentPlayerCharacter()
    {
        Transaction assignPlayerTx = new()
        {
            FeePayer = sessionWallet.Account,
            Instructions = new List<TransactionInstruction>(),
            RecentBlockHash = (await rpcClient.GetLatestBlockHashAsync()).Result.Value.Blockhash
        };

        var client = new SolDungeonsClient(Web3.Rpc, Web3.WsRpc, programId);
        var res = await client.GetUserAsync(playerPDA);

        PublicKey.TryFindProgramAddress(new[]{
            Encoding.UTF8.GetBytes(res.ParsedResult.CurrentCharacterId.ToString()),
            Web3.Account.PublicKey.KeyBytes
        }, programId, out PublicKey playerCharacterPDA, out var _);

        var accountsAssignCharacter = new LockCurrentUserCharacterAccounts()
        {
            Signer = sessionWallet.Account.PublicKey,
            User = playerPDA,
            UserCharacter = playerCharacterPDA,
            SessionToken = sessionWallet.SessionTokenPDA,
            SystemProgram = SystemProgram.ProgramIdKey
        };

        var playerCharacterIx = SolDungeonsProgram.LockCurrentUserCharacter(
            accountsAssignCharacter,
            programId
        );

        assignPlayerTx.Add(playerCharacterIx);

        RequestResult<string> resTx = await sessionWallet.SignAndSendTransaction(assignPlayerTx);
        Debug.Log($"Result: {Newtonsoft.Json.JsonConvert.SerializeObject(resTx)}");
    }
}
