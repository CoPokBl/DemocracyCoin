SHA256 hashing still

- Variable reward
- Variable difficulty
- Update variables every x blocks
- Save UTC timestamp
- Nodes should save a balance database for wallets to quickly perform validation (it can be constructed with the blocks)
- Max transactions per block
- Better wallet addresses (copy btc/eth)
- HANDLE double spending before transaction is processed

Data to be hashed: block header

Block Data:
- Header:
    - Network Version
    - Prev Block Header Hash
    - Merkle Root
    - Timestamp
    - Difficulty
    - Nonce
- Transactions

Transaction Data:
- Sender wallet
- Reciever wallet
- Send amount
- Transaction fee
- Signature (from priv key of all prev data concat and hashed)

TODO:
- Use bouncy castle for SHA256 hashing (or other lib)
- Don't reserialize the block header every time
- Godot wallet
- HTTP/TCP local api for other apps to interact with the wallet
- Test TCP nodes
- Balance miner rewards
- DB caching wrapper
