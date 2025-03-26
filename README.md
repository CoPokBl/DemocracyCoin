# Democracy Coin
My little cryptocurrency experiment. 
An entire blockchain system written from scratch in C#.

## ConsoleNode
A testing node that can be run from the console. 
It will connect to other nodes and mine blocks.

## DemCoinLib
The core library for the entire system. Contains all common
code and schemas for the blockchain. All other projects
depend on this.

## DemNodeTcpLib
This library extends the core libraries node class to enable
TCP communication between nodes.

## GodotWallet
A simple wallet application written in Godot.

## Testing
A project that contains unit tests to protect against regressions
in all other projects.
