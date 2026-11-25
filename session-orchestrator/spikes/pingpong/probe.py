"""The test commands of the spike: probe.py <head> <command>. It sends one line to the pipe of a head and prints the answer."""
import sys
import time


def send(head, line, wait=3):
    end = time.time() + wait
    while True:
        try:
            with open(rf"\\.\pipe\pingpong-{head}", "r+b", buffering=0) as pipe:
                pipe.write((line + "\n").encode("utf-8"))
                answer = b""
                while not answer.endswith(b"\n"):
                    part = pipe.read(4096)
                    if not part:
                        break
                    answer += part
                return answer.decode("utf-8", "replace").strip()
        except OSError as error:
            if time.time() > end:
                return f"no pipe: {error}"
            time.sleep(0.1)


if __name__ == "__main__":
    print(send(sys.argv[1], " ".join(sys.argv[2:])))
