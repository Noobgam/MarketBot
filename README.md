## Status

The project is discontinued

## What is this?

This project was forked from Jessecar96's [SteamBot](https://github.com/Jessecar96/SteamBot), a fully automated trading software made specifically for http://market.csgo.com. 

## How to use it?

Ordinary usecase is as follows:

You should have multiple pods (instances of software) running. 
One being the master node, others being worker nodes.

To compile master node compile it with `CORE` constant defined.
Master node is used to load-balance between steam inventories and monitor running pods.

Without having master node, all worker nodes act independently of each other,
with no possibility of load-balancing (thus overflowing steam inventory limits).

## Configuring the Bot

Master node domain should either be hard-coded Global config in code or overriden via settings.json.
`settings.json` should be defined as [follows](https://gist.githubusercontent.com/Noobgam/8aa9b32b6b147b69f2ffc2057f75652e/raw/e545b5db3cd7f9b331832033cc324b3f2ef1330f/full_config.json)

If `settings.json` is not specified it will be pulled via HTTP request from master node.

To run master pass arguments in format `<port> <MasterConfigUrl>` [MasterConfig.json](https://gist.githubusercontent.com/Noobgam/8aa9b32b6b147b69f2ffc2057f75652e/raw/e545b5db3cd7f9b331832033cc324b3f2ef1330f/full_config.json)

To run worker pass arguments in format `<WorkerConfigUrl> <SteamBot default options(optional)>` [WorkerConfig.json](https://gist.githubusercontent.com/Noobgam/819841a960112ae85fe8ac61b6bd33e1/raw/97cc2ed7ce38da301369352a4331b7029d31dd05/config.json)

One could potentially run multiple steam accounts from the same machine, yet this is
not recommendded due to Steam API being highly unreliable and severe restrictions could apply
when running more than a dozen accounts due to Steam authentification protocols.

## Requirements

Worker and master nodes require MongoDB to store logs,
keep track of market sale history (for further use by analysts),
frequently update ban list, since scamming on steam is very frequent.

It is recommended to run single `mongod` replicaset for master and all worker nodes.

## More help?
A list of contributors:

This bot:

- [Noobgam](https://github.com/Noobgam) (project lead)
- [AndreySmirdin](https://github.com/AndreySmirdin) analyst

SteamBot contributors:

- [Jessecar96](http://steamcommunity.com/id/jessecar) (project lead)
- [geel9](http://steamcommunity.com/id/geel9)
- [cwhelchel](http://steamcommunity.com/id/cmw69krinkle)
- [Lagg](http://lagg.me)
- [BlueRaja](http://steamcommunity.com/id/BlueRaja/)
- [Noobgam](https://github.com/Noobgam)