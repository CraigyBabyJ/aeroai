# AeroAI

AeroAI is an AI-powered Air Traffic Control (ATC) simulator that provides realistic ATC communications for flight simulation.

## Features

- Realistic ATC phraseology and procedures
- Support for IFR clearances, taxi instructions, takeoff clearances, and approach vectors
- Integration with OpenAI's language models for natural ATC responses
- Flight context tracking and state management
- Vectoring support for departures and arrivals

## Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/CraigyBabyJ/aeroai.git
   cd aeroai
   ```

2. Copy the example environment file:
   ```bash
   cp .env.example .env
   ```

3. Edit `.env` and add your OpenAI API key:
   ```
   OPENAI_API_KEY=sk-your-actual-api-key-here
   ```

4. Build the project:
   ```bash
   dotnet build
   ```

5. Run the application:
   ```bash
   dotnet run
   ```

## Configuration

See `.env.example` for all available configuration options.

## Requirements

- .NET 8.0 or later
- OpenAI API key (or compatible API endpoint)

## License

[Add your license here]

# aeroai
