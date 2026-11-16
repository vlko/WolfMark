#!/usr/bin/env python3
"""Print the titles of the X11 windows on $DISPLAY, one per line (tools/smoke-linux.sh; no xdotool needed).

Reads _NET_WM_NAME (UTF-8, what Avalonia sets) with WM_NAME as the fallback, from every window in the tree.
"""
import ctypes
import ctypes.util
import sys

x11 = ctypes.cdll.LoadLibrary(ctypes.util.find_library("X11") or "libX11.so.6")
Window = ctypes.c_ulong
Atom = ctypes.c_ulong
x11.XOpenDisplay.restype = ctypes.c_void_p
x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
x11.XDefaultRootWindow.restype = Window
x11.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
x11.XInternAtom.restype = Atom
x11.XInternAtom.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
x11.XQueryTree.argtypes = [ctypes.c_void_p, Window, ctypes.POINTER(Window), ctypes.POINTER(Window),
                           ctypes.POINTER(ctypes.POINTER(Window)), ctypes.POINTER(ctypes.c_uint)]
x11.XGetWindowProperty.argtypes = [ctypes.c_void_p, Window, Atom, ctypes.c_long, ctypes.c_long, ctypes.c_int, Atom,
                                   ctypes.POINTER(Atom), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_ulong),
                                   ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_void_p)]
x11.XFree.argtypes = [ctypes.c_void_p]


def prop(display, window, name, kind):
    actual, fmt, count, after, data = Atom(), ctypes.c_int(), ctypes.c_ulong(), ctypes.c_ulong(), ctypes.c_void_p()
    status = x11.XGetWindowProperty(display, window, x11.XInternAtom(display, name, 0), 0, 4096, 0,
                                    x11.XInternAtom(display, kind, 0), ctypes.byref(actual), ctypes.byref(fmt),
                                    ctypes.byref(count), ctypes.byref(after), ctypes.byref(data))
    if status != 0 or not data.value:
        return None
    try:
        return ctypes.string_at(data.value, count.value).decode("utf-8", "replace") if count.value else None
    finally:
        x11.XFree(data)


def main():
    display = x11.XOpenDisplay(None)
    if not display:
        sys.exit("cannot open display")
    stack = [x11.XDefaultRootWindow(display)]
    while stack:
        window = stack.pop()
        title = prop(display, window, b"_NET_WM_NAME", b"UTF8_STRING") or prop(display, window, b"WM_NAME", b"STRING")
        if title:
            print(title)
        root, parent, children, count = Window(), Window(), ctypes.POINTER(Window)(), ctypes.c_uint()
        if x11.XQueryTree(display, window, ctypes.byref(root), ctypes.byref(parent), ctypes.byref(children), ctypes.byref(count)):
            stack.extend(children[i] for i in range(count.value))
            if children:
                x11.XFree(children)


if __name__ == "__main__":
    main()
