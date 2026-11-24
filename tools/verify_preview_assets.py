# -*- coding: utf-8 -*-
"""Dispatch the compiled asset-only checks through the game's Python console."""
import System, coroutine
from System.Reflection import BindingFlags
from System.IO import File

ROOT = 'E:/project/c#/KoiSync/'


def run():
    assembly = System.Reflection.Assembly.Load(File.ReadAllBytes(ROOT + 'tests/KoiSync.PreviewChecks/bin/Release/net35/KoiSync.PreviewAssetChecks.dll'))
    method = assembly.GetType('PreviewChecks').GetMethod('Run', BindingFlags.Public | BindingFlags.Static)
    method.Invoke(None, System.Array[System.Object]([ROOT + 'artifacts/preview-assets/v1.0.0-build03/runtime/']))


coroutine.start_new_coroutine(run, (), {})
