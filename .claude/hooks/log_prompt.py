#!/usr/bin/env python3
"""
Hook UserPromptSubmit: registra automaticamente cada prompt enviado pelo
usuario neste projeto em used-prompts/log.md (append-only), para permitir
que entrevistadores repliquem/avaliem o uso de IA no desenvolvimento.

Le o payload JSON do hook via stdin, extrai o texto do prompt, calcula o
proximo numero sequencial olhando as entradas ja existentes no log, e
adiciona uma nova entrada no mesmo formato ja usado no arquivo.
"""
import json
import re
import sys
from datetime import date
from pathlib import Path

LOG_FILE = Path(__file__).resolve().parents[2] / "used-prompts" / "log.md"
ENTRY_HEADER_RE = re.compile(r"^### \d{4}-\d{2}-\d{2} — (\d+)\s*$", re.MULTILINE)


def read_prompt_text(payload: dict) -> str:
    # Nomes de campo candidatos para o texto do prompt, dependendo da versao
    # do harness. Usa o primeiro que existir e nao for vazio.
    for key in ("prompt", "user_prompt", "message", "text"):
        value = payload.get(key)
        if isinstance(value, str) and value.strip():
            return value
    return ""


def next_sequence_number(log_text: str) -> int:
    numbers = [int(match) for match in ENTRY_HEADER_RE.findall(log_text)]
    return (max(numbers) + 1) if numbers else 1


def main() -> int:
    # Le stdin como bytes crus e decodifica explicitamente como UTF-8. Nao
    # usar sys.stdin.read() (texto): no Windows ele decodifica usando o
    # codepage do console (nao UTF-8) por padrao, mesmo a pipe chegando com
    # bytes corretos — corrompe qualquer acentuacao (bug real encontrado e
    # corrigido em 2026-09-04, ver used-prompts/log.md).
    raw_stdin = sys.stdin.buffer.read().decode("utf-8", errors="replace")
    try:
        payload = json.loads(raw_stdin) if raw_stdin.strip() else {}
    except json.JSONDecodeError:
        payload = {}

    prompt_text = read_prompt_text(payload)
    if not prompt_text:
        return 0

    LOG_FILE.parent.mkdir(parents=True, exist_ok=True)
    # utf-8-sig: le e descarta o BOM se presente, sem afetar o parsing do
    # numero sequencial. O arquivo carrega um unico BOM UTF-8 (compatibilidade
    # com ferramentas nativas do Windows, ex. Notepad) — nunca duplicar.
    existing_text = (
        LOG_FILE.read_text(encoding="utf-8-sig") if LOG_FILE.exists() else ""
    )
    file_has_bom = (
        LOG_FILE.exists() and LOG_FILE.read_bytes()[:3] == b"\xef\xbb\xbf"
    )

    seq = next_sequence_number(existing_text)
    today = date.today().isoformat()

    entry = f"\n---\n\n### {today} — {seq:03d}\n\n{prompt_text.strip()}\n"

    if not LOG_FILE.exists():
        # arquivo novo: grava com BOM desde o inicio
        with LOG_FILE.open("w", encoding="utf-8-sig") as f:
            f.write(entry.lstrip("\n"))
    elif not file_has_bom:
        # arquivo existente sem BOM (nao deveria acontecer apos a correcao,
        # mas defensivo): adiciona o BOM agora, uma unica vez
        with LOG_FILE.open("w", encoding="utf-8-sig") as f:
            f.write(existing_text + entry)
    else:
        # caminho normal: BOM ja existe no inicio do arquivo, so apendar
        # (utf-8 simples, sem gerar um segundo BOM no meio do arquivo)
        with LOG_FILE.open("a", encoding="utf-8") as f:
            f.write(entry)

    return 0


if __name__ == "__main__":
    sys.exit(main())
