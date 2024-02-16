<?php

use BookStack\Api\ApiToken;
use BookStack\Users\UserRepo;
use BookStack\Users\Models;
use BookStack\Facades\Theme;
use BookStack\Theming\ThemeEvents;
use Illuminate\Console\Command;
use Symfony\Component\Console\Command\Command as SymfonyCommand;

class TestTokenCommand extends Command
{
    protected $signature = 'bookstack:test-api-token';
    protected $description = 'Generate API tokens for testing.';

    public function handle(UserRepo $userRepo)
    {
        $admin = $userRepo->getByEmail('admin@admin.com');
        if ($admin == null)
        {
            $this->error('admin user not found.');
            return SymfonyCommand::FAILURE;
        }
        
        $name = 'TestToken';
        $token = $admin->apiTokens()->where('name', '=', $name)->first();
        if ($token != null)
        {
            $this->info("Test token '{$name}' already exists");
            return SymfonyCommand::SUCCESS;
        }
        
        $token_id     = env('CUSTOM_TEST_TOKEN_ID');
        $token_secret = env('CUSTOM_TEST_TOKEN_SECRET');
        if (!$token_id)     $token_id     = '00001111222233334444555566667777';
        if (!$token_secret) $token_secret = '88889999aaaabbbbccccddddeeeeffff';

        if (ApiToken::query()->where('token_id', '=', $token_id)->exists())
        {
            $this->error('Cannot be created because the token ID already exists.');
            return SymfonyCommand::FAILURE;
        }
        
        $token = (new ApiToken())->forceFill([
            'name'       => $name,
            'token_id'   => $token_id,
            'secret'     => Hash::make($token_secret),
            'user_id'    => $admin->id,
            'expires_at' => ApiToken::defaultExpiry(),
        ]);
        
        $token->save();
        
        $this->info("Test token '{$name}' successfully created.");
        return SymfonyCommand::SUCCESS;
    }
}

Theme::registerCommand(new TestTokenCommand);
