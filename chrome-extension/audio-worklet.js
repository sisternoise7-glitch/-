class PcmTapProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.framesPerPacket = Math.max(1, Math.round(sampleRate / 2));
    this.buffer = new Float32Array(this.framesPerPacket);
    this.length = 0;
  }

  process(inputs, outputs) {
    const input = inputs[0];
    const output = outputs[0];
    if (!input?.length) return true;

    const channels = input.length;
    const frameCount = input[0].length;
    for (let channel = 0; channel < output.length; channel += 1) {
      output[channel].set(input[Math.min(channel, channels - 1)]);
    }

    for (let frame = 0; frame < frameCount; frame += 1) {
      let mixed = 0;
      for (let channel = 0; channel < channels; channel += 1) mixed += input[channel][frame] || 0;
      this.buffer[this.length++] = mixed / channels;
      if (this.length === this.buffer.length) {
        const packet = this.buffer;
        this.buffer = new Float32Array(this.framesPerPacket);
        this.length = 0;
        this.port.postMessage(packet, [packet.buffer]);
      }
    }
    return true;
  }
}

registerProcessor('pcm-tap', PcmTapProcessor);
