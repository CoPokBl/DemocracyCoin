using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;
using DemCoinLib;
using DemCoinLib.Structs;
using DemNodeTcpLib;
using Godot;
using Environment = System.Environment;

namespace DemocracyCoinWallet.pages;

public partial class Main : Control {
	private Label _balanceLabel;  // onready
	private Label _walletAddress;  // onready
	private Label _miningStatus;  // onready
	private Button _toggleMining;  // onready
	private SpinBox _miningThreads;  // onready
	private SpinBox _sendAmount;  // onready
	private LineEdit _sendPayee;  // onready
	private TextEdit _sendMessage;  // onready
	private LineEdit _importWords;  // onready

	private AcceptDialog _transactionSent;  // onready
	private AcceptDialog _errorBox;  // onready
	private AcceptDialog _walletExported;  // onready
	private AcceptDialog _importWallet;  // onready
	
	private DemCoinWallet _wallet;
	private DemCoinNode _node;
	private TcpDemNode _tcp;
	private double _balance = -1;
	private GenericMiner _miner;
	private readonly Stopwatch _miningClock = new();
	private CancellationTokenSource _miningToken;
	private int _minedBlocks;
	private string _error;
	private readonly Queue<AcceptDialog> _triggeredDialogs = new();
	
	public override void _Ready() {
		_balanceLabel = GetNode<Label>("%Balance");
		_walletAddress = GetNode<Label>("%WalletAddress");
		_miningStatus = GetNode<Label>("%MiningStatus");
		_toggleMining = GetNode<Button>("%ToggleMining");
		_miningThreads = GetNode<SpinBox>("%MiningThreads");
		_sendAmount = GetNode<SpinBox>("%SendAmount");
		_sendPayee = GetNode<LineEdit>("%Payee");
		_sendMessage = GetNode<TextEdit>("%SendMessage");
		_importWords = GetNode<LineEdit>("%WalletXml");
		_transactionSent = GetNode<AcceptDialog>("TransactionSent");
		_errorBox = GetNode<AcceptDialog>("Error");
		_walletExported = GetNode<AcceptDialog>("WalletExported");
		_importWallet = GetNode<AcceptDialog>("ImportWallet");
		GetNode<Button>("%RefreshBalance").Pressed += RefreshClicked;
		GetNode<Button>("%CopyAddress").Pressed += CopyWalletAddress;
		GetNode<Button>("%SendMoney").Pressed += SendMoney;
		GetNode<Button>("%ExportWallet").Pressed += ExportWallet;
		GetNode<Button>("%ImportWallet").Pressed += ImportWallet;
		_toggleMining.Pressed += ToggleMining;
		_importWallet.Confirmed += StartWalletImport;

		if (!File.Exists("wallet.txt")) {
			GD.Print("No wallet found. Creating one...");
    
			_wallet = DemCoinWallet.NewWithPhrase();
			File.WriteAllText("wallet.txt", _wallet.SeedPhrase);
			GD.Print("A new wallet has been created: " + _wallet.SeedPhrase);
		}
		else {
			string walletXml = File.ReadAllText("wallet.txt");
			_wallet = DemCoinWallet.FromPhrase(walletXml.Split(' '));
		}

		GD.Print("Using wallet: " + _wallet.Address);
		_walletAddress.Text = _wallet.Address;

		_miningThreads.Value = Environment.ProcessorCount;
		
		_node = new DemCoinNode("blockchain.db");
		_tcp = _node.CreateTcpNode(port:9211, peers:["127.0.0.1:9534"]);
		_tcp.Log += m => GD.Print($"[TCP] {m}");
		_tcp.Init();
		
		RefreshBalance();

		_miner = new GenericMiner(_node, _wallet);
		_miner.MinedBlock += MineBlock;
	}

	private void StartWalletImport() {
		DemCoinWallet newWallet;
		try {
			newWallet = DemCoinWallet.FromPhrase(_importWords.Text.Split(' '));
		}
		catch (Exception) {
			_error = "Invalid wallet Phrase.";
			return;
		}

		_wallet = newWallet;
		RefreshBalance();
	}

	private void ImportWallet() {
		_importWords.Text = "";
		_triggeredDialogs.Enqueue(_importWallet);
	}

	private void ExportWallet() {
		DisplayServer.ClipboardSet(_wallet.SeedPhrase);
		_triggeredDialogs.Enqueue(_walletExported);
	}

	private void SendMoney() {
		string payeeAddress = _sendPayee.Text;
		if (string.IsNullOrWhiteSpace(payeeAddress)) {
			_error = "Payee address cannot be empty.";
			return;
		}
		byte[] addressBytes;
		try {
			addressBytes = DemCoinUtils.AddressStringToBytes(payeeAddress);
		}
		catch (Exception) {
			_error = "Invalid payee address";
			return;
		}

		double amount = _sendAmount.Value;
		if (amount == 0) {
			_error = "Amount cannot be zero.";
			return;
		}
		Transaction transaction = new() {
			TransactionNumber = _node.GetNextTransactionNumber(_wallet.Address),
			Amount = amount,
			Sender = _wallet.PublicKey,
			Recipient = addressBytes,
			TransactionFee = 0
		};
		transaction.SetMessage(_sendMessage.Text);
		transaction.Sign(_wallet);

		_sendPayee.Text = "";
		_sendAmount.Value = 0;
		_sendMessage.Text = "";
		GD.Print("Submitting transaction");

		if (!_node.ValidateTransaction(transaction, out string? failReason)) {
			throw new Exception($"Failed to publish transaction (balance: {_node.GetBalance(transaction.SenderAddress)}): {failReason}");
		}
		
		_node.PublishTransaction(transaction);
	}

	private void MineBlock(Block block) {
		GD.Print("Block mined");
		_node.MineBlock(block);
		_minedBlocks++;
		RefreshBalance();
	}

	private void ToggleMining() {
		bool mine = _miningToken == null;

		if (!mine) {
			_miningToken.Cancel();
			_miningToken = null;
			_toggleMining.Text = "Start Mining";
			_miningClock.Stop();
			return;
		}
		
		// Start mining
		_miningToken = new CancellationTokenSource();
		_miner.CheckedNonces = 0;
		GD.Print("Mining with " + (int) _miningThreads.Value + " threads");
		_miner.MineAsync(_miningToken.Token, (int) _miningThreads.Value);
		_miningClock.Restart();
		_toggleMining.Text = "Stop Mining";
		GD.Print("Started mining at difficulty: " + _node.GetCurrentDifficulty());
	}

	private void UpdateMiningStatusLabel() {
		double hps = _miningClock.Elapsed.TotalSeconds == 0 ? 0 : _miner.CheckedNonces / _miningClock.Elapsed.TotalSeconds;
		 string actualTimePerBlock = _minedBlocks == 0
		 	? "\u221e"
		 	: TimeSpan.FromSeconds(Math.Round(_miningClock.Elapsed.TotalSeconds / _minedBlocks)).ToString();

		 if (_miner == null) {
			 _miningStatus.Text = "Miner is null";
			 return;
		 }
		 
		 string status = _miningToken == null ? "Disabled" : "Enabled";
		 BigInteger cDiff = _node.GetCurrentDifficulty(cache: true);
		 double cReward = _node.GetCurrentMinerReward(cache: true);
		 
		 _miningStatus.Text = $"Status: {status}\n" +
		                      $"Hashes: {_miner?.CheckedNonces ?? throw new Exception("Miner null")} ({Math.Round(hps)} hashes/second)\n" +
		                      $"Difficulty: {cDiff}\n" +
		                      $"Reward: {cReward}\n" +
		                      $"Time Spent: {_miningClock?.Elapsed ?? throw new Exception("null miner clock")} ({actualTimePerBlock} seconds/block)\n" +
		                      $"Blocks Mined: {_minedBlocks}";
	}

	private void CopyWalletAddress() {
		DisplayServer.ClipboardSet(_wallet.Address);
	}

	private void RefreshClicked() {
		GD.Print("Refresh clicked");
		RefreshBalance();
	}

	/// <summary>
	/// Request a refresh of the current wallet balance.
	/// </summary>
	private void RefreshBalance() {
		_balance = _node.GetBalance(_wallet.Address);
	}

	public override void _Process(double delta) {
		if (Math.Abs(_balance + 1) > 0.0001) {
			_balanceLabel.Text = $"{Math.Round(_balance, 4)} dc";
		}
		UpdateMiningStatusLabel();

		if (_error != null) {
			_errorBox.DialogText = _error;
			_errorBox.Visible = true;
			_error = null;
		}

		if (_triggeredDialogs.Count > 0) {
			_triggeredDialogs.Dequeue().Visible = true;
		}
	}
}