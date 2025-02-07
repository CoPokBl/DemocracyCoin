using System.Diagnostics;
using System.Numerics;
using DemCoinLib.Db;
using DemCoinLib.Structs;

namespace DemCoinLib;

public class DemCoinNode(string dbFile = "blockchain.db") {
    // NETWORK CONSTS
    public const string NetworkVersion = "DemCoin v0.0.1";
    public const int DiffAdjustmentInterval = 10;  // How many blocks before we adjust the difficulty
    public const int TargetSecsPerBlock = 60;  // How many seconds we want a block to take to mine
    public const int MaxAllowedTimeDrift = 60;  // How many seconds we allow a block to be off by
    public const int MaxAdjustmentFactor = 4;  // How much we allow the difficulty to change by (diff * factor or diff / factor)
    public const int BigMaxAdjustmentFactor = 4;  // How much we allow the difficulty to change by (diff * factor or diff / factor)
    public const int BigAdjustmentBlocks = 100;  // How many blocks during which we can make bigger adjustments to the difficulty
    public const double MaxMinerReward = 20;  // The maximum reward a miner can get for mining a block
    public const double MinMinerReward = 0.2;  // The minimum reward a miner can get for mining a block
    public static readonly byte[] CoinbasePublicKey = new byte[64];  // The public key of the coinbase, used to specify a coinbase transaction, should also be used as signature
    
    // RUNTIME SETTINGS
    public string CoinbaseMessage = "Hello World!";
    public double MinimumTransactionFee = 0;  // The minimum fee a transaction must have to be added to the blockchain by us.
    public string[] FreeTransactionWallets = [];  // A list of wallets who we will process transactions for regardless of fee (both as sender or receiver).
    
    // EVENTS
    public event Action<ulong, Block> OnBlockMined = (_, _) => { };
    
    // PROPERTIES
    public Block LastBlock => BlockDatabase.GetLastBlock()!;
    public ulong ChainHeight => BlockDatabase.GetBlockCount();
    public int TargetTimePerInterval => DiffAdjustmentInterval * TargetSecsPerBlock;  // How long we want the adjustment interval to take
    
    public IBlockDatabase BlockDatabase = null!;
    private readonly List<Transaction> _pendingTransactions = [];  // These are currently volatile.

    private readonly object _pendingTransactionsLock = new();
    private readonly object _mineLock = new();

    public void Init() {
        BlockDatabase = new BlockDatabaseSqlite(dbFile);

        if (ChainHeight == 0) {
            AddChainStartBlock();
        }
    }

    public bool ValidateChain() {
        ulong chainHeight = ChainHeight;
        for (ulong i = 0; i < chainHeight; i++) {
            Block block = BlockDatabase.GetBlockByIndex(i)!;
            if (ValidateBlock(block, chainHeight - i)) {
                Console.WriteLine($"Block {i} is valid");
            }
            else {
                Console.WriteLine($"Block {i} is invalid!");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Generate a new block that should be mined next.
    ///
    /// The block will have no nonce set. You must change the nonce to a valid one.
    /// </summary>
    /// <returns>The new block.</returns>
    public Block GenerateNewBlock(DemCoinWallet coinbaseRecipient) {
        Transaction coinbase = new() {
            // Coinbase, we get a reward :)
            Sender = CoinbasePublicKey,
            Recipient = coinbaseRecipient.AddressBytes,
            Amount = 1, // TODO
            Signature = CoinbasePublicKey,
            TransactionNumber = 0,
            TransactionFee = 0
        };
        coinbase.SetMessage(CoinbaseMessage);
        
        // Get all pending transactions
        List<Transaction> transactions = [
            coinbase
        ];
        
        transactions.AddRange(CollectTransactions(out double fees));
        coinbase.Amount += fees;  // Add the fees to the coinbase

        Block block = new() {
            PrevHeaderHash = LastBlock.HashHeader(),
            Transactions = transactions.ToArray(),
            TimeStamp = DemCoinUtils.GetUtcTimestamp(),
            Difficulty = GetCurrentDifficulty()
        };
        block.SetNetworkVersion(NetworkVersion);
        block.CalculateTransactionsSig();
        
        return block;
    }
    
    /// <summary>
    /// Collects transactions to add to a new block.
    ///
    /// Configure the minimum transaction fee with <see cref="MinimumTransactionFee"/>.
    /// Ignores transactions with a fee less than the minimum.
    /// </summary>
    /// <returns></returns>
    public List<Transaction> CollectTransactions(out double totalFee) {
        lock (_pendingTransactionsLock) {
            List<Transaction> transactions = [];
            totalFee = 0;

            foreach (Transaction transaction in _pendingTransactions) {
                bool vip = FreeTransactionWallets.Contains(DemCoinUtils.AddressBytesToString(transaction.Recipient)) || 
                           FreeTransactionWallets.Contains(DemCoinUtils.PublicKeyToAddress(transaction.Sender));  // Whether to prioritise

                if (transaction.TransactionFee < MinimumTransactionFee && !vip) {
                    continue;
                }
                
                transactions.Add(transaction);
                totalFee += transaction.TransactionFee;
            }

            return transactions;
        }
    }

    /// <summary>
    /// Calculate the difficulty of mining the next block on the chain.
    /// </summary>
    /// <param name="skip">How many blocks to go back in the database for calculate for.</param>
    /// <param name="db">Override db to use to perform the calculation.</param>
    /// <returns></returns>
    public byte[] GetCurrentDifficulty(ulong skip = 0, IBlockDatabase? db = null) {
        db ??= BlockDatabase;
        
        if ((db.GetBlockCount() - skip) % DiffAdjustmentInterval != 0) {
            return db.GetLastBlock(skip)?.Difficulty ?? DemCoinUtils.ToInt256Bytes(1);
        }
        
        // Calculate new difficulty
        Block firstBlock = db.GetBlockByIndex(db.GetBlockCount() - skip - DiffAdjustmentInterval)!;
        Block lastBlock = db.GetLastBlock(skip)!;
        
        ulong timeTaken = lastBlock.TimeStamp - firstBlock.TimeStamp;

        int maxAdjustment = db.GetBlockCount() - skip <= BigAdjustmentBlocks ? BigMaxAdjustmentFactor : MaxAdjustmentFactor;
        
        double adjustment = timeTaken / (double)TargetTimePerInterval;
        if (adjustment > maxAdjustment) {
            adjustment = maxAdjustment;
        }

        if (adjustment < 1.0 / maxAdjustment) {
            adjustment = 1.0 / maxAdjustment;
        }
        
        BigInteger lastDiff = new(lastBlock.Difficulty);
        lastDiff *= (BigInteger)adjustment;

        return DemCoinUtils.ToInt256Bytes(lastDiff);
    }
    
    public double GetCurrentMinerReward() {  // Gets reward for next block based on current difficulty
        return 1;  // TODO: Make this based on difficulty
    }
    
    public void AddChainStartBlock() {
        BlockDatabase.InsertBlock(GetDefBlock());
    }

    /// <summary>
    /// Gets the genesis block, can be used as a test block.
    /// </summary>
    /// <returns></returns>
    public static Block GetDefBlock() {
        Block block = new() {
            PrevHeaderHash = new byte[32],
            TimeStamp = 0,
            Difficulty = DemCoinUtils.ToInt256Bytes(new BigInteger(1)),
            Nonce = new byte[8],
            Transactions = []
        };
        block.SetNetworkVersion(NetworkVersion);
        block.CalculateTransactionsSig();
        return block;
    }

    public void MineBlock(Block block) {
        lock (_pendingTransactionsLock) lock (_mineLock) {
            if (!ValidateBlock(block, out string? failReason)) {
                throw new Exception("Invalid block: " + failReason);
            }
        
            AddBlockToDatabase(block);
            Debug.Assert(ValidateBlock(LastBlock, 1), "Block added to database incorrectly");

            foreach (Transaction transaction in block.Transactions) {
                _pendingTransactions.Remove(transaction);
            }
            
            OnBlockMined.Invoke(ChainHeight - 1, block);
        }
    }

    /// <summary>
    /// Creates a transaction sending money to a wallet and queues it to be added to the blockchain
    /// when the next block is mined.
    /// </summary>
    /// <remarks>
    /// Transaction will not be executed until a block is mined.
    /// </remarks>
    /// <param name="wallet">Your wallet.</param>
    /// <param name="to">Recipient of funds.</param>
    /// <param name="amount">Amount to transfer.</param>
    /// <param name="fee">The fee to offer for verifying the transaction.</param>
    /// <param name="msg">The message to include in the transaction.</param>
    public void SendMoney(DemCoinWallet wallet, string to, double amount, double fee, string msg = "") {
        Transaction transaction = new() {
            Sender = wallet.PublicKey,
            Recipient = DemCoinUtils.AddressStringToBytes(to),
            Amount = amount,
            TransactionFee = fee,
            TransactionNumber = GetNextTransactionNumber(wallet.Address)
        };
        transaction.SetMessage(msg);
        transaction.Sign(wallet);

        PublishTransaction(transaction);
    }

    public void PublishTransaction(Transaction transaction) {
        if (!ValidateTransaction(transaction)) {
            throw new Exception("Cannot publish invalid transaction");
        }
        lock (_pendingTransactionsLock) {
            _pendingTransactions.Add(transaction);
        }
    }
    
    public double GetBalance(string walletAddress) {
        // TODO: Handle double spending before transaction is published
        return BlockDatabase.GetBalance(walletAddress);
    }

    public ulong GetNextTransactionNumber(string walletAddress, bool ignorePending = false) {
        if (!ignorePending) lock (_pendingTransactionsLock) {  // Check to see if they have any pending transactions
            Transaction? lastPending = _pendingTransactions.LastOrDefault(t => DemCoinUtils.PublicKeyToAddress(t.Sender) == walletAddress);
            if (lastPending != null) {
                return lastPending.TransactionNumber + 1;
            }
        }
        return BlockDatabase.GetLastTransactionNumber(walletAddress) + 1;
    }

    public bool IsBlockNonceValid(Block block, bool calcTs = true) {
        return IsHashValidBlock(block.HashHeader(calcTs), GetCurrentDifficulty());
    }

    private static bool IsHashValidBlock(IReadOnlyList<byte> hash, byte[] difficulty) {
        return IsHashValidBlock(hash, new BigInteger(difficulty));
    }

    private static bool IsHashValidBlock(IReadOnlyList<byte> hash, BigInteger difficulty) {
        // Since 'difficulty' is a 64-bit number, any hash with non-zero
        // data in the top 192 bits (24 bytes) is automatically larger.
        for (int i = 0; i < 24; i++) {
            if (hash[i] != 0)
                return true; // 256-bit hash is definitely > 64-bit difficulty
        }

        // If the top 24 bytes are all zero, parse the last 8 bytes
        // (big-endian) into a 64-bit integer and compare.
        ulong value = 0;
        for (int i = 24; i < 32; i++) {
            value = (value << 8) | hash[i];
        }

        Console.WriteLine("Checking that " + value + " is more than " + difficulty);
        return value > difficulty;
    }

    /// <summary>
    /// This method adds a block to the database.
    ///
    /// It is assumed that the block is valid.
    /// </summary>
    /// <param name="block">A valid block.</param>
    private void AddBlockToDatabase(Block block) {
        BlockDatabase.InsertBlock(block);
        
        // Transactions
        foreach (Transaction transaction in block.Transactions) {
            BlockDatabase.InsertTransaction(transaction, ChainHeight-1);
        }
    }

    /// <summary>
    /// Checks the blocks in order accounting for each previous block in the validations.
    /// </summary>
    /// <param name="blocks">The blocks to validate.</param>
    /// <param name="failReason">The rejection reason.</param>
    /// <param name="skip">How many blocks to go back into the database to begin.</param>
    /// <param name="checkTimestamp">Whether to check the timestamp (You probably don't want this).</param>
    /// <param name="checkTransactions">Whether to validate transactions in each block.</param>
    /// <returns>Whether the block is valid.</returns>
    public bool ValidateBlocks(Block[] blocks, out string? failReason, ulong skip = 0, bool checkTimestamp = true,
        bool checkTransactions = true) {
        ExtendedBlockDatabase db = new(BlockDatabase, skip);

        foreach (Block block in blocks) {
            bool success = ValidateBlock(block, out failReason, 0, checkTimestamp, checkTransactions, db, false);
            if (!success) return false;
            db.InsertBlock(block);
        }

        failReason = null;
        return true;
    }

    public bool ValidateBlock(Block block, ulong skip = 0, bool checkTimestamp = true, bool checkTransactions = true, IBlockDatabase? db = null) {
        return ValidateBlock(block, out _, skip, checkTimestamp, checkTransactions, db);
    }

    /// <summary>
    /// Checks the validity of a Block.
    /// </summary>
    /// <param name="block">The block to validate.</param>
    /// <param name="failReason">The reason that the check failed, if it passed then ignore this.</param>
    /// <param name="skip">
    /// How far back in the database the block is, the age of the block.
    /// Set to 0 if the block is not in the database and this block comes after the last block in the database.
    /// Set to 1 if the being validated is the last block in the database.
    /// It is how many blocks we need to skip before we get to the block that should come before this block.
    /// Defaults to 0.
    /// </param>
    /// <param name="checkTimestamp">Whether to validate the timestamp of the block</param>
    /// <param name="checkTransactions">Whether to check the validity of the transactions.</param>
    /// <param name="db">Override database to use.</param>
    /// <param name="calcTransactionSigs">Whether to calculate transaction signatures, if set to false, this will be avoided.</param>
    /// <returns>True if the block is valid, otherwise false.</returns>
    public bool ValidateBlock(Block block, out string? failReason, ulong skip = 0, bool checkTimestamp = true, bool checkTransactions = true, IBlockDatabase? db = null, bool calcTransactionSigs = true) {
        failReason = null;
        db ??= BlockDatabase;
        Block lastBlock = db.GetLastBlock(skip)!;
        
        if (skip == ChainHeight) {  // We are validating the genesis block, it must match our version
            failReason = "Invalid genesis block";  // Just ignore if the sequence is equal
            return block.HashHeader(calcTransactionSigs).SequenceEqual(GetDefBlock().HashHeader(calcTransactionSigs));
        }
        
        byte[] expectedHash = lastBlock.HashHeader(calcTransactionSigs);
        if (!block.PrevHeaderHash.SequenceEqual(expectedHash)) {
            failReason = "Invalid PrevHeaderHash";
            return false;
        }

        if (!IsBlockNonceValid(block, false)) {
            failReason = "Invalid nonce";
            return false;
        }

        if (!block.Difficulty.SequenceEqual(GetCurrentDifficulty(skip, db))) {
            failReason = "Incorrect difficulty";
            return false;
        }

        if (checkTransactions) {
            if (block.Transactions.Length == 0) {
                failReason = "No transactions (Coinbase required)";
                return false;
            }

            bool allTransactionsValid = true;
            double totalFees = 0;
            Transaction? coinbase = null;
            Dictionary<string, ulong> transactionNumbers = new();
            foreach (Transaction transaction in block.Transactions) {
                if (transaction.Sender.SequenceEqual(CoinbasePublicKey)) {  // coinbase
                    if (coinbase != null) {
                        failReason = "Multiple coinbase transactions";
                        allTransactionsValid = false;
                        break;
                    }
                
                    coinbase = transaction;
                    continue;
                }
            
                // EVERYTHING HERE IS NOT CHECKED FOR COINBASE TRANSACTIONS
                if (skip == 0 && !ValidateTransaction(transaction, out string? badTransReason, transactionNumbers)) {
                    failReason = $"Transaction invalid ({badTransReason})";
                    allTransactionsValid = false;
                    break;
                }
            
                totalFees += transaction.TransactionFee;
            }
        
            // Validate coinbase
            if (coinbase == null || coinbase.Amount > totalFees + GetCurrentMinerReward()) {
                failReason = coinbase == null ? "Missing coinbase" : "Incorrect coinbase reward amount";
                return false;
            }

            if (!allTransactionsValid) {
                // Reason should already be set
                return false;
            }
        
            // Check the transactions sig
            byte[] existingSig = block.TransactionsSig;
            block.CalculateTransactionsSig();
            if (!existingSig.SequenceEqual(block.TransactionsSig)) {
                failReason = "Invalid transactions signature";
                return false;
            }
        }

        if (checkTimestamp && Math.Abs((double)DemCoinUtils.GetUtcTimestamp() - block.TimeStamp) > MaxAllowedTimeDrift) {
            failReason = "Invalid timestamp";
            return false;
        }

        return true;
    }

    public bool ValidateTransaction(Transaction transaction, Dictionary<string, ulong>? transactionNumbers = null) {
        return ValidateTransaction(transaction, out _, transactionNumbers);
    }

    /// <summary>
    /// Check the validity of a Transaction object that has not been added to the database.
    /// This works will pending transactions.
    /// This method logs the fail reason upon rejection.
    /// </summary>
    /// <remarks>
    /// THIS WILL NOT WORK ON TRANSACTIONS ALREADY IN THE DATABASE. It will fail because the transaction number will be invalid.
    /// </remarks>
    /// <param name="transaction">The transaction to validate, MUST NOT BE IN DATABASE and must not be coinbase.</param>
    /// <param name="failReason">The reason that the transaction is invalid. Ignore if this returns true.</param>
    /// <param name="transactionNumbers">A map of transaction numbers to use to override the DB.</param>
    /// <returns>True if the transaction is valid, otherwise false.</returns>
    public bool ValidateTransaction(Transaction transaction, out string? failReason, Dictionary<string, ulong>? transactionNumbers = null) {
        transactionNumbers ??= new Dictionary<string, ulong>();
        
        if (transaction.Amount <= 0) {  // You have to actually send something
            failReason = "Negative or zero amount";
            return false;
        }

        if (transaction.TransactionFee < 0) {  // You can't have a negative fee
            failReason = "Negative fee";
            return false;
        }
            
        // Check if sender has enough money
        double senderBalance = BlockDatabase.GetBalance(transaction.SenderAddress);
        if (senderBalance < transaction.Amount + transaction.TransactionFee) {
            failReason = "Insufficient funds";
            return false;
        }
            
        // Check if transaction is valid
        if (!transaction.IsSignatureValid()) {
            failReason = "Invalid signature";
            return false;
        }
            
        // Check if the transaction number is valid
        ulong nextTn = transactionNumbers.GetValueOrDefault(transaction.SenderAddress, GetNextTransactionNumber(transaction.SenderAddress, true));
        if (transaction.TransactionNumber != nextTn) {
            failReason = "Incorrect transaction number";
            return false;
        }
        transactionNumbers[transaction.SenderAddress] = nextTn + 1;

        failReason = null;
        return true;
    }
    
}