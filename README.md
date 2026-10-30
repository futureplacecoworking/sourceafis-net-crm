## Agente Windows FS80H da Central

Este fork inclui um aplicativo de bandeja para Windows 10/11 x64, com captura Futronic FS80H,
comparação SourceAFIS local e autorização pela Central de Operações.
O app usa a identidade visual da Central, com páginas **Leitura** e **Configurações**,
orientações de captura em destaque e fontes/arte incorporadas para uso offline da interface.
O pacote autocontido e o instalador por usuário são gerados pelo workflow **Agente FS80H — Windows**.
[Instalação, pareamento e operação em português](docs/AGENTE-FS80H.md).
Nenhuma fechadura é acionada nesta etapa. Driver e DLL oficiais são obtidos separadamente do fabricante.

[![SWUbanner](https://raw.githubusercontent.com/vshymanskyy/StandWithUkraine/main/banner2-direct.svg)](https://github.com/vshymanskyy/StandWithUkraine/blob/main/docs/README.md)

# SourceAFIS for .NET

[![Build status](https://github.com/robertvazan/sourceafis-net/actions/workflows/build.yml/badge.svg)](https://github.com/robertvazan/sourceafis-net/actions/workflows/build.yml)
[![Nuget](https://img.shields.io/nuget/v/SourceAFIS)](https://www.nuget.org/packages/SourceAFIS/)

SourceAFIS for .NET is a pure C# port of [SourceAFIS](https://sourceafis.machinezoo.com/),
an algorithm for recognition of human fingerprints.
It can compare two fingerprints 1:1 or search a large database 1:N for matching fingerprint.
It takes fingerprint images on input and produces similarity score on output.
Similarity score is then compared to customizable match threshold.

More on [homepage](https://sourceafis.machinezoo.com/net).

## Status

Stable and maintained.

## Getting started

See [homepage](https://sourceafis.machinezoo.com/net).

## Documentation

* [SourceAFIS for .NET](https://sourceafis.machinezoo.com/net)
* [XML doc comments](https://github.com/robertvazan/sourceafis-net/tree/master/SourceAFIS)
* [SourceAFIS overview](https://sourceafis.machinezoo.com/)
* [Algorithm](https://sourceafis.machinezoo.com/algorithm)

## Feedback

Bug reports and pull requests are welcome. See [CONTRIBUTING.md](https://github.com/robertvazan/sourceafis-net/blob/master/CONTRIBUTING.md).

## License

Distributed under [Apache License 2.0](https://github.com/robertvazan/sourceafis-net/blob/master/LICENSE).
